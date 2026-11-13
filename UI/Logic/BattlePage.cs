using Ros.UI.Main;
using System.Collections.Generic;
using Ros.Transport;
using FairyGUI;
using UnityEngine;

using Ros.Config;
public class BattlePage : PageBase
{
    private readonly UI_BattlePanel panel;

    private float skillCdRefreshTimer;   // 冷却环重渲染节流（本地推演需要周期刷新）
    private float nextQuickMessageTime;  // 快捷消息发送节流（防刷）
    private readonly List<UI_SkillList.Slot> skillSlots = new(); // 技能栏快照缓冲（每 0.1s 重填一次，避免分配）
    private readonly Dictionary<ushort, UI_EntityBar> entityBars = new();
    // 血量缓存：摘要包只在 healthDirty/全量时携带血量段，未携带的包（如仅 buff 变化）不能把血条刷成 0/0
    private readonly Dictionary<ushort, (int health, int maxHealth)> entityVitals = new();
    private readonly List<UI_EntityBar.Buff> barBuffs = new(); // 血条 buff 快照缓冲（每个摘要包重填）
    private readonly List<SCEntityDisplayInfo.BuffRuntime> localBuffs = new();                      // 本地玩家左上角血条的 buff 数据源
    private readonly float barDefaultSizeDistance = GlobalVariable.I(global_variablesEnum.distance_of_bar_default_size); // 血条 scale=(1,1) 时的相机距离
    private readonly float labelDefaultSizeDistance = GlobalVariable.I(global_variablesEnum.distance_of_label_default_size); // 飘字 scale=(1,1) 时的相机距离
    private UI_SkillTip skillTip;     // 技能悬浮提示（技能栏条目）
    private UI_BuffTip buffTip;       // buff 悬浮提示（任意 buff 图标）
    private UI_QuickMessagePanel quickMessagePanel; // 快捷指令轮盘（挂在战斗信息面板里）
    private readonly List<UI_QuickMessagePointer> quickMessagePointers = new(); // 快捷指令点位（挂在世界层）
    private GTextField levelLabel;    // 玩家栏等级数字（FGUI 侧未命名，按 n12 取）
    private int localPlayerCamp = -1; // 本地玩家阵营（随本地实体摘要更新；小地图敌我识别用）
    private bool spectatorMode;       // 纯观战方玩家（无战斗实体）
    private bool localPlayerAlive = true; // 本地玩家实体是否在场（死亡观战期间 false，显示观战下拉）
    private bool reviveActive;        // 本地玩家死亡等待中：复活条由客户端按配表本地推演
    private float reviveDuration;     // 本段复活总时长（revive_time.csv，按死亡次数与阵营取）
    private float reviveElapsed;      // 本段已等待时长（起点由服务器包里的 progress 折算）
    private int spectateTargetClientId = -1;  // 当前观战目标玩家 id（-1 = 未观战）
    private int spectateTargetEntityId = -1;  // 观战目标实体 id（-1 = 等待其表现数据到达后绑定相机）
    private float spectatePendingSince;   // 发起观战的时刻（目标数据迟迟不到 = 目标已死，超时重选）
    private float onlookRefreshTimer;     // 观战下拉选项刷新节流
    private bool syncingOnlookCombo;      // 程序刷新下拉选项期间屏蔽 onChanged 回调

    private const int HitVfxIndexBuff = 0;   // 命中特效列表下标：buff/DoT 类（贴实体中心）
    private const int HitVfxIndexDirect = 1; // 命中特效列表下标：直接伤害（普通/暴击/护盾）
    private const float HitVfxLife = 2f;
    private readonly Queue<string> importantBannerQueue = new(); // 待播的特殊技能播报文案，按到达顺序逐条播
    private bool importantBannerPlaying;                          // 当前 banner 动画是否在播
    private const string ImportantBannerPrefix = "特殊能力启动-";
    private readonly List<int> settingTabKeys = new();   // 页签 → 配表 key（按 key 排序）
    private readonly List<string> settingTabTexts = new(); // 每个页签对应的说明文本
    private int settingTab; // 当前选中的页签下标
    private int godViewPressCount;        // 上帝视角连按计数
    private float godViewFirstPressTime;  // 本轮第一次按下的时刻（超窗口重算）

    // 本局会话数据全部来自加载期（不再读组队缓存）
    private static ClientLoadingManager Session => ClientLoadingManager.Current;
    private static bool HasSession => Session != null && Session.HasSession;

    // 客户端技能状态（持有表 / 技能栏 / 冷却库存）全在它身上
    private static ClientSkillManager Skills => Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;

    // 剩余时间推演的唯一持有者（起点由服务器快照校准，见 ClientBattleTimeManager）
    private static ClientBattleTimeManager BattleTime => Tool.ClientLogicManager != null ? Tool.ClientLogicManager.BattleTime : null;

    private static ushort OwnEntityId => HasSession ? Session.OwnEntityId : (ushort)0;
    private bool IsOwnEntity(int entityId) => HasSession && OwnEntityId == entityId;

    // 技能栏挂在玩家栏里（UI_PlayerPanel.m_skill_list）
    private UI_SkillList SkillBar => panel.m_playerPanel != null ? panel.m_playerPanel.m_skill_list : null;

    public BattlePage(UI_BattlePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override void Construct()
    {
        var battleInfo = panel.m_BattleInfoPanel;
        battleInfo.m_btn_settings.onClick.Add(ShowSettings);
        battleInfo.m_btn_quick_message.onClick.Add(ToggleQuickMessage);

        var settings = panel.m_SettingsPanel;
        settings.m_panel.m_close.onClick.Add(HideSettings);
        settings.m_settings_scroll_view.m_back_to_fight.onClick.Add(HideSettings);
        settings.m_settings_scroll_view.m_exit.onClick.Add(OnExitClicked); // 退出需二次确认（对局中退出不可逆）
        BuildSettingTips();

        // 快捷指令轮盘挂在战斗信息面板里，FGUI 侧还没命名
        quickMessagePanel = battleInfo.GetChild("n5") as UI_QuickMessagePanel;
        if (quickMessagePanel == null)
            throw new System.FormatException("[战斗界面] BattleInfoPanel 的第 6 个子件不是快捷指令面板，检查 FGUI 结构");
        quickMessagePanel.Build(SendQuickMessage);

        panel.m_showRegenerationBar.selectedIndex = 0; // 复活进度默认隐藏
        panel.m_playerPanel.m_buffList.m_list.itemRenderer =
            (i, obj) => ((UI_buffItem)obj).Fill(localBuffs[i].type, localBuffs[i].level, ShowBuffTip, HideBuffTip);
        levelLabel = panel.m_playerPanel.GetChild("n12") as GTextField; // 等级数字在 FGUI 侧还没命名
        panel.m_skillSelectPanel.Build(OnSkillSelectCommitted);
        panel.m_skillSelectPanel.Close();
        panel.m_selectCardPanel.Build(OnCardSelected, OnCardDiscarded, null); // 关闭 = 保留待抽次数
        panel.m_selectCardPanel.CloseImmediately();
        panel.m_list_selectCard.itemRenderer = RenderCardButton;
        panel.m_largeMinimapPanel.visible = false;
        panel.m_largeMinimapPanel.touchable = false;
        panel.m_largeMinimapPanel.m_panel.m_close.onClick.Set(panel.m_largeMinimapPanel.Close);
        if (panel.m_Minimap != null && panel.m_Minimap.m_minimapButton != null)
            panel.m_Minimap.m_minimapButton.onClick.Set(OpenLargeMinimap); // 点小地图打开大地图，关闭走底框按钮
        panel.m_godViewPanel.Init(ShowBuffTip, HideBuffTip, ShowSkillTip, HideSkillTip);
        panel.m_godViewPanel.Close();
        panel.m_godViewPanel.m_panel.m_close.onClick.Set(panel.m_godViewPanel.Close);
    }

    // 设置面板常驻战斗界面，开关只改显隐与点击拦截
    private void ShowSettings()
    {
        panel.m_SettingsPanel.visible = true;
        panel.m_SettingsPanel.touchable = true;
    }

    private void HideSettings()
    {
        panel.m_SettingsPanel.visible = false;
        panel.m_SettingsPanel.touchable = false;
    }

    // 快捷指令轮盘：点信息面板上的按钮开关；展开时按键 = 发送并收起（按住 J 时附带相机瞄准点）
    private void ToggleQuickMessage()
    {
        if (quickMessagePanel == null) return;
        if (quickMessagePanel.IsOpen) quickMessagePanel.Close();
        else quickMessagePanel.Open();
    }

    private void HideQuickMessage() => quickMessagePanel?.Close();

    private void HandleQuickMessageKey(int index)
    {
        if (quickMessagePanel == null) return;
        if (!quickMessagePanel.IsOpen)
        {
            quickMessagePanel.Open();
            return;
        }
        quickMessagePanel.Close();
        SendQuickMessage(index);
    }

    private void SendQuickMessage(int index)
    {
        float now = Time.time;
        if (now < nextQuickMessageTime) return; // 防刷：同玩家 1s 一条
        nextQuickMessageTime = now + GlobalConfig.ai_message_min_interval;

        var message = new CSQuickMessage() { messageId = index };
        if (Input.GetKey(GlobalConfig.melee_key) && TryGetAimPoint(out var aim))
        {
            message.hasTargetPosition = true;
            message.targetPosition = aim;
        }
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendQuickMessage(message);
    }

    // 相机屏幕中心射线与地面 y=0 的交点；取不到就按"无位置"发送
    private static bool TryGetAimPoint(out Vector3 point)    {
        point = default;
        var cam = Tool.CameraController != null ? Tool.CameraController.WorldCamera : null;
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        if (Mathf.Abs(ray.direction.y) < 0.0001f) return false;
        float distance = -ray.origin.y / ray.direction.y;
        if (distance <= 0f) return false;
        point = ray.origin + ray.direction * distance;
        return true;
    }

    #region 快捷指令点位
    // 收到同阵营的快捷指令：按配表「点位显示时长」生成一个场景点位，0 = 不生成（上报状况）
    private void OnQuickMessagePointer(SCQuickMessage message)
    {
        if (message == null) return;
        float duration = QuickMessageConfig.Get(message.messageId).markerDuration;
        if (duration <= 0f) return;

        var pointer = UI_QuickMessagePointer.CreateInstance();
        panel.m_world_element_container.AddChild(pointer); // 与血条/飘字同层
        pointer.Bind(message, duration);
        quickMessagePointers.Add(pointer);
        PlaceQuickMessagePointer(pointer, UiScale); // 先落一次位，避免第一帧出现在容器左上角
    }

    private void TickQuickMessagePointers()
    {
        if (quickMessagePointers.Count == 0) return;
        float now = Time.unscaledTime;
        float uiScale = UiScale;
        for (int i = quickMessagePointers.Count - 1; i >= 0; i--)
        {
            var pointer = quickMessagePointers[i];
            if (pointer.Expired(now))
            {
                pointer.DestroySelf();
                quickMessagePointers.RemoveAt(i);
                continue;
            }
            PlaceQuickMessagePointer(pointer, uiScale);
        }
    }

    // 随包没带位置时改用发送者实体当前位置；实体已消失则由点位自己停在最后已知位置
    private void PlaceQuickMessagePointer(UI_QuickMessagePointer pointer, float uiScale)
    {
        Vector3 senderPos = Vector3.zero;
        bool hasSenderPos = false;
        if (HasSession)
        {
            int entityId = Session.GetEntityId(pointer.SenderClientId);
            var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
            if (entityId > 0 && players != null) hasSenderPos = players.TryGetEntityPosition((ushort)entityId, out senderPos);
        }
        pointer.Place(senderPos, hasSenderPos, uiScale);
    }

    private void ClearQuickMessagePointers()
    {
        foreach (var pointer in quickMessagePointers) pointer.DestroySelf();
        quickMessagePointers.Clear();
    }
    #endregion

    private static bool AnyQuickMessageKeyDown(out int index)
    {
        var keys = GlobalConfig.quick_message_keys;
        for (int i = 0; i < keys.Length; i++)
        {
            if (!Input.GetKeyDown(keys[i])) continue;
            index = i;
            return true;
        }
        index = -1;
        return false;
    }

    // 说明来自 setting_tips：规则页签按配表行生成，点页签把该行内容填进右上角的正文
    private void BuildSettingTips()
    {
        var keys = new List<int>(SettingTipsConfig.content.Keys);
        keys.Sort();
        foreach (int key in keys)
        {
            settingTabKeys.Add(key);
            settingTabTexts.Add(SettingTipsConfig.content[key].text.Replace(@"{\n}", "\n"));
        }

        var view = panel.m_SettingsPanel.m_settings_scroll_view;
        var tabs = view.m_rule_buttons;
        tabs.itemRenderer = (i, obj) =>
        {
            var tab = obj.asButton;
            // 页签标题写在条目自带的文本上（FGUI 里这个文本没命名）
            var label = tab.GetChild("n3") as GTextField;
            if (label != null) label.text = SettingTipsConfig.content[settingTabKeys[i]].name;
            tab.selected = i == settingTab;
            int index = i;
            tab.onClick.Set(() => SelectSettingTab(index));
        };
        view.m_rule_content.UBBEnabled = true; // 配表内容带 UBB 标记（加粗 / 颜色）

        tabs.numItems = settingTabKeys.Count;
        if (settingTabKeys.Count > 0) SelectSettingTab(0);
    }

    private void SelectSettingTab(int index)
    {
        settingTab = index;
        var view = panel.m_SettingsPanel.m_settings_scroll_view;
        for (int i = 0; i < view.m_rule_buttons.numChildren; i++)
            view.m_rule_buttons.GetChildAt(i).asButton.selected = i == index; // 页签选中态

        view.m_rule_content.text = settingTabTexts[index];
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        EventManager.AddEvent<SCEntityDisplayInfo>(ClientEvent.OnEntityDisplayUpdate, OnEntityDisplayUpdate);
        EventManager.AddEvent<int>(ClientEvent.OnEntityDisplayRemove, OnEntityDisplayRemove);
        EventManager.AddEvent<SCMinimapEntity>(ClientEvent.OnMinimapUpdate, OnMinimapUpdate);
        EventManager.AddEvent<float>(ClientEvent.OnMinimapRadiusUpdate, OnMinimapRadiusUpdate);
        EventManager.AddEvent<SCGodViewEntities>(ClientEvent.OnGodViewEntities, OnGodViewEntities);
        EventManager.AddEvent<SCGodViewCorals>(ClientEvent.OnGodViewCorals, OnGodViewCorals);
        EventManager.AddEvent<SCGodViewEntityDetail>(ClientEvent.OnGodViewDetail, OnGodViewDetail);
        EventManager.AddEvent<SCScoreInfo>(ClientEvent.OnScoreUpdate, OnScoreUpdate);
        EventManager.AddEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.AddEvent<SCCombatLabel>(ClientEvent.OnCombatLabelDisplay, OnCombatLabelDisplay);
        EventManager.AddEvent<SCReviveInfo>(ClientEvent.OnReviveProgressUpdate, OnReviveProgressUpdate);
        EventManager.AddEvent<SCCampAliveCount>(ClientEvent.OnCampAliveCount, OnCampAliveCount);
        EventManager.AddEvent<SCDefensePointProgress>(ClientEvent.OnDefensePointProgress, OnDefensePointProgress);
        EventManager.AddEvent(ClientEvent.OnSkillBarRefresh, OnSkillBarRefresh);
        EventManager.AddEvent<SCPlayerProgress>(ClientEvent.OnPlayerProgress, OnPlayerProgress);
        EventManager.AddEvent<SCQuickMessage>(ClientEvent.OnQuickMessage, OnQuickMessagePointer);
        if (Tool.CameraController != null) Tool.CameraController.OnCameraUpdated += OnCameraUpdated;

        localPlayerCamp = -1;
        spectatorMode = Session != null && Session.IsSpectator;
        localPlayerAlive = !spectatorMode;
        panel.m_onlook.selectedIndex = spectatorMode ? 1 : 0; // onlook=1：FGUI 自动隐藏血条/技能等非观战元素
        if (panel.m_onlookComboBox != null) panel.m_onlookComboBox.onChanged.Set(OnOnlookComboBoxChanged);
        RefreshCampAliveCount(NetworkManager.LatestCampAliveCount); // 进入战斗先填一次缓存值，随后由变更包刷新
        // 首页/大厅展示的选角预览模型只属于那两个界面，进战斗前清掉（否则会残留在地图的预览锚点上）
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.Hide();
        ResetGuardStatues();
        HideSettings(); // 设置面板每局从收起状态开始
        HideQuickMessage(); // 快捷消息面板每局从收起状态开始
        ResetGodViewPress(); // 连按计数每局清零
        HideImportantBanner(); // 重要事件 banner 每局从隐藏状态开始
        OnDefensePointProgress(NetworkManager.LatestDefensePointProgress); // 进入战斗先填一次缓存值，随后由变更包刷新
        reviveActive = false; // 复活条每局从收起状态开始，重新由 SCReviveInfo 驱动
        ClearEntityBars();
        ClearQuickMessagePointers(); // 快捷指令点位只属于本局
        HideAllTips();
        localBuffs.Clear();
        panel.m_playerPanel.m_buffList.m_list.numItems = 0;
        panel.m_Minimap?.Reset(GlobalVariable.L(global_variablesEnum.minimap_view_radius)[0]); //每局回到默认档（首档），与服务器 ClearBattleState 对齐
        panel.m_largeMinimapPanel.Close();
        panel.m_godViewPanel.Close();
        panel.m_skillSelectPanel.Close();
        panel.m_selectCardPanel.CloseImmediately();
        ResetCardState();
        Skills?.ResetBattle();
        BuildSkillSlots();
        RefreshTimeIcon();
    }

    public override void Exit()
    {
        base.Exit();
        EventManager.RemoveEvent<SCEntityDisplayInfo>(ClientEvent.OnEntityDisplayUpdate, OnEntityDisplayUpdate);
        EventManager.RemoveEvent<int>(ClientEvent.OnEntityDisplayRemove, OnEntityDisplayRemove);
        EventManager.RemoveEvent<SCMinimapEntity>(ClientEvent.OnMinimapUpdate, OnMinimapUpdate);
        EventManager.RemoveEvent<float>(ClientEvent.OnMinimapRadiusUpdate, OnMinimapRadiusUpdate);
        EventManager.RemoveEvent<SCGodViewEntities>(ClientEvent.OnGodViewEntities, OnGodViewEntities);
        EventManager.RemoveEvent<SCGodViewCorals>(ClientEvent.OnGodViewCorals, OnGodViewCorals);
        EventManager.RemoveEvent<SCGodViewEntityDetail>(ClientEvent.OnGodViewDetail, OnGodViewDetail);
        EventManager.RemoveEvent<SCScoreInfo>(ClientEvent.OnScoreUpdate, OnScoreUpdate);
        EventManager.RemoveEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.RemoveEvent<SCCombatLabel>(ClientEvent.OnCombatLabelDisplay, OnCombatLabelDisplay);
        EventManager.RemoveEvent<SCReviveInfo>(ClientEvent.OnReviveProgressUpdate, OnReviveProgressUpdate);
        EventManager.RemoveEvent<SCCampAliveCount>(ClientEvent.OnCampAliveCount, OnCampAliveCount);
        EventManager.RemoveEvent<SCDefensePointProgress>(ClientEvent.OnDefensePointProgress, OnDefensePointProgress);
        EventManager.RemoveEvent(ClientEvent.OnSkillBarRefresh, OnSkillBarRefresh);
        EventManager.RemoveEvent<SCPlayerProgress>(ClientEvent.OnPlayerProgress, OnPlayerProgress);
        EventManager.RemoveEvent<SCQuickMessage>(ClientEvent.OnQuickMessage, OnQuickMessagePointer);
        if (Tool.CameraController != null) Tool.CameraController.OnCameraUpdated -= OnCameraUpdated;
        spectatorMode = false;
        localPlayerAlive = true;
        spectateTargetClientId = -1;
        spectateTargetEntityId = -1;
        if (panel.m_onlook != null) panel.m_onlook.selectedIndex = 0;
        panel.m_largeMinimapPanel.Close();
        panel.m_godViewPanel.Close();
        panel.m_skillSelectPanel.Close();
        panel.m_selectCardPanel.CloseImmediately();
        ResetCardState();
        HideAllTips();
        HideImportantBanner();
        ClearQuickMessagePointers();
        // 离开战斗界面即销毁地形场景（Culler 与内容根一起销毁，点位数据一并复位）
        if (Tool.ModularTerrainGenerator != null) Tool.ModularTerrainGenerator.Clear();
    }

    public override void Tick(float deltaTime)
    {
        // 键盘被 UI 占用（GM 面板 / 文字输入）时，技能选择、快捷消息与面板快捷键都不响应
        if (!InputManager.KeysCapturedByUi)
        {
            if (Input.GetKeyDown(GlobalConfig.skill_select_key)) ToggleSkillSelect();
            if (AnyQuickMessageKeyDown(out int quickMessageIndex)) HandleQuickMessageKey(quickMessageIndex);
            TickGodViewOpenKey();
        }
        //剩余时间（以快照校准的本地推演为准，重连/中途进入的起点由服务器补发）
        if (HasSession && BattleTime != null && panel.m_BattleInfoPanel.m_time_left != null)
            panel.m_BattleInfoPanel.m_time_left.text = FormatTime(Mathf.Max(0f, BattleTime.RemainTime));
        RefreshTimeIcon();
        TickMinimap();
        TickMinimapPanels(deltaTime);
        TickSpectator(deltaTime);
        TickLocalSkillCd(deltaTime);
        TickReviveProgress(deltaTime);
        TickQuickMessagePointers();
    }

    // 冷却推演在 ClientSkillManager，这里只按节流重渲染冷却环与库存数
    private void TickLocalSkillCd(float deltaTime)
    {
        if (spectatorMode || SkillBar == null || !SkillBar.IsBuilt) return;
        skillCdRefreshTimer -= deltaTime;
        if (skillCdRefreshTimer > 0f) return;
        skillCdRefreshTimer = 0.1f;
        RefreshSkillBar();
    }

    // 持有表变化 / 选择面板应用 / 消耗回执：下一帧立刻重画技能栏
    private void OnSkillBarRefresh()
    {
        if (SkillBar == null || !SkillBar.IsBuilt) BuildSkillSlots(); // 首包到达时兜底搭栏
        skillCdRefreshTimer = 0f;
        panel.m_skillSelectPanel.RefreshHeld(Skills?.HeldSkills); // 打开期间新获得的技能要进列表
    }

    #region 事件处理
    private void OnEntityDisplayUpdate(SCEntityDisplayInfo info)
    {
        if (info == null) return;
        // 血条是否创建由 entity.csv 的「显示血条」列决定；名牌只给双方角色
        if (EntityConfig.GetByCategory(info.type).showHealthBar == true)
        {
            bool withName = info.type.category == EntityCategory.Character_Attack ||
                            info.type.category == EntityCategory.Character_Defense;
            UpdateEntityBar(info, withName);
        }

        if (IsOwnEntity(info.entityId))
        {
            localPlayerCamp = (int)info.camp;
            localPlayerAlive = true;
            panel.m_onlook.selectedIndex = 0; // 复活/正常出战：血条与技能栏回到显示
            spectateTargetClientId = -1; // 本地实体出现（复活/正常出战）：服务器已退出观战，视角回自身
            spectateTargetEntityId = -1;
            UpdateLocalPlayerBar(info);
        }
        // 纯观战无本地实体：以观战目标阵营作为「我方」基准（名牌敌我配色、小地图点位敌我识别）
        else if (spectatorMode && info.entityId == spectateTargetEntityId) localPlayerCamp = (int)info.camp;
    }

    private void OnEntityDisplayRemove(int entityId)
    {
        RemoveEntityBar((ushort)entityId);
        entityVitals.Remove((ushort)entityId);
        panel.m_Minimap?.Remove((ushort)entityId);

        if (IsOwnEntity(entityId))
        {
            localPlayerAlive = false; // 本地死亡：显示观战下拉，进入死亡观战
            panel.m_onlook.selectedIndex = 1; // 死亡等待复活：由 FGUI gear 收起血条与技能栏
        }
    }

    #region 观战
    private void TickSpectator(float deltaTime)
    {
        var box = panel.m_onlookComboBox;
        if (box == null) return;
        box.visible = spectatorMode || !localPlayerAlive; // 观战目标下拉：纯观战/死亡观战可见

        // 纯观战未选目标（进场自动选 / 目标死亡后自动重选）：随机挑一名参战单位（真人或 AI）。
        // 不能在此 return，否则每帧都卡在重选、下面的下拉刷新永远执行不到，列表会一直是空的。
        if (spectatorMode && spectateTargetClientId == -1) SpectateTo(PickRandomCombatMember());
        if (spectateTargetClientId != -1)
        {
            // 观战目标的当前实体每帧对齐一次（关系在加载会话里维护）：复活换实体自动重绑，无实体则重选
            int currentEntity = ResolveSpectateEntityId(spectateTargetClientId);
            if (currentEntity <= 0)
            {
                spectateTargetClientId = -1;
                spectateTargetEntityId = -1;
                spectatePendingSince = 0f;
            }
            else
            {
                if (currentEntity != spectateTargetEntityId)
                {
                    spectateTargetEntityId = currentEntity;
                    spectatePendingSince = HasSpectateView(currentEntity) ? 0f : Time.time;
                }
                // 目标实体视图未建（目标已死亡 / 表现数据没到）：3 秒后重选
                if (HasSpectateView(spectateTargetEntityId)) spectatePendingSince = 0f;
                else if (spectatePendingSince > 0f && Time.time - spectatePendingSince > 3f)
                {
                    spectateTargetClientId = -1;
                    spectateTargetEntityId = -1;
                    spectatePendingSince = 0f;
                }
            }
        }
        // 目标表现数据已到达：绑定相机到其视图锚点（锚点由视图逐帧刷新）
        if (spectateTargetEntityId != -1 && Tool.CameraController != null
            && Tool.ClientLogicManager != null && Tool.ClientLogicManager.EntityPlayers != null)
        {
            var cam = Tool.CameraController;
            var anchor = Tool.ClientLogicManager.EntityPlayers.GetOrCreateCameraAnchor((ushort)spectateTargetEntityId);
            // 自由视角下只记目标不摆位，否则每帧会被拉回目标身位
            if (anchor != null && (cam.lookTarget != anchor || cam.IsFree))
                cam.SetLookTarget(anchor, !cam.IsFree);
        }

        onlookRefreshTimer -= deltaTime;
        if (onlookRefreshTimer <= 0f)
        {
            onlookRefreshTimer = 0.5f;
            RefreshOnlookComboBox();
        }
    }

    // 观战目标：真人（正数 id）与 AI（虚拟负数 id，从 -1000 起）都合法；-1 仅作「无目标」哨兵
    private void SpectateTo(int targetClientId)
    {
        if (targetClientId == -1) return;
        spectateTargetClientId = targetClientId;
        spectateTargetEntityId = ResolveSpectateEntityId(targetClientId);
        spectatePendingSince = HasSpectateView(spectateTargetEntityId) ? 0f : Time.time;
        NetworkManager.RequestSpectate(targetClientId);
    }

    private int ResolveSpectateEntityId(int clientId) => HasSession ? Session.GetEntityId(clientId) : -1;

    private static bool HasSpectateView(int entityId)
    {
        if (entityId <= 0) return false;
        return Tool.ClientLogicManager != null && Tool.ClientLogicManager.EntityPlayers != null
            && Tool.ClientLogicManager.EntityPlayers.GetView((ushort)entityId) != null;
    }

    private int PickRandomCombatMember()
    {
        if (!HasSession) return -1;
        var units = Session.GetCombatants();
        if (units == null || units.Count == 0) return -1;
        return units[Random.Range(0, units.Count)].clientId;
    }

    private void RefreshOnlookComboBox()
    {
        var box = panel.m_onlookComboBox;
        if (box == null) return;
        var names = new List<string>();
        var values = new List<string>();
        if (HasSession)
        {
            var units = Session.GetCombatants();
            for (int i = 0; i < units.Count; i++)
            {
                var unit = units[i];
                if (unit == null) continue;
                if (unit.clientId == NetworkManager.LocalToken) continue; // 自己不进观战列表
                names.Add(Session.GetName(unit.clientId));
                values.Add(unit.clientId.ToString());
            }
        }
        syncingOnlookCombo = true;
        box.values = values.ToArray();
        box.items = names.ToArray();
        box.selectedIndex = values.IndexOf(spectateTargetClientId.ToString());
        syncingOnlookCombo = false;
    }

    private void OnOnlookComboBoxChanged()
    {
        if (syncingOnlookCombo) return;
        var box = panel.m_onlookComboBox;
        if (box == null || box.selectedIndex < 0 || box.values == null
            || box.selectedIndex >= box.values.Length) return;
        if (int.TryParse(box.values[box.selectedIndex], out var target)) SpectateTo(target);
    }
    #endregion

    // 技能栏条目数 = 本局角色技能槽位数
    private void BuildSkillSlots()
    {
        int count = Skills != null ? Skills.SlotCapacity : 0;
        if (count <= 0) count = 1; // 观战/属性取不到时至少露出一格
        SkillBar?.Build(count, ShowSkillTip, HideSkillTip);
        RefreshSkillBar();
    }

    // 只做取数：槽位技能、恢复进度、库存次数来自 ClientSkillManager，绘制归技能栏自身
    private void RefreshSkillBar()
    {
        if (SkillBar == null || !SkillBar.IsBuilt) return;
        skillSlots.Clear();
        int count = SkillBar.SlotCount;
        var recharge = Skills != null ? Skills.Recharge : null;
        for (int i = 0; i < count; i++)
        {
            int skillId = Skills != null ? Skills.GetSlotSkillId(i) : -1;
            if (skillId < 0)
            {
                skillSlots.Add(new UI_SkillList.Slot(-1, 1f, 0, true));
                continue;
            }
            // 无限库存没有充能概念，不显示次数
            bool unlimited = SkillManager.GetSkillStore(skillId) < 0;
            skillSlots.Add(new UI_SkillList.Slot(skillId,
                unlimited || recharge == null ? 1f : recharge.GetCycleProgress(skillId),
                unlimited || recharge == null ? 0 : recharge.GetStore(skillId),
                unlimited));
        }
        SkillBar.Refresh(skillSlots);
    }

    #region 技能选择面板
    private void ToggleSkillSelect()
    {
        if (spectatorMode || !HasSession || SkillBar == null || !SkillBar.IsBuilt) return;
        if (panel.m_skillSelectPanel.IsOpen) panel.m_skillSelectPanel.Commit(); // 关闭即应用，与关闭按钮同一路径
        else panel.m_skillSelectPanel.Open(Skills?.HeldSkills, Skills?.ActiveSkills, Skills != null ? Skills.SlotCapacity : 0);
    }

    // 关闭回调：勾选集合按技能 id 升序进栏（排序在 ClientSkillManager 里做）
    private void OnSkillSelectCommitted()
    {
        if (Skills != null) Skills.TrySetActiveSkills(panel.m_skillSelectPanel.Selection);
        RefreshSkillBar();
    }
    #endregion

    #region 守护点（主点 + 3 个副点）
    private readonly int[] guardianHealth = { -1, -1, -1, -1 }; // 当前血量，-1 = 还没收到/还没生成

    // 只认 SCDefensePointProgress 的当前血量；上限、名字读 entity.csv，百分比在客户端算
    private void OnDefensePointProgress(SCDefensePointProgress info)
    {
        if (info == null) return;
        guardianHealth[0] = info.mainHealth;
        guardianHealth[1] = info.sub1Health;
        guardianHealth[2] = info.sub2Health;
        guardianHealth[3] = info.sub3Health;
        RefreshGuardianPanels();
    }

    private void ResetGuardStatues()
    {
        for (int i = 0; i < guardianHealth.Length; i++) guardianHealth[i] = -1;
        RefreshGuardianPanels(); // 复位成满血/未摧毁，等第一包覆盖
    }

    private void RefreshGuardianPanels()
    {
        var bars = panel.m_barPanel;
        if (bars == null) return;
        ApplyMainGuardian(bars.m_main, 0);
        ApplySubGuardian(bars.m_sub1, 1);
        ApplySubGuardian(bars.m_sub2, 2);
        ApplySubGuardian(bars.m_sub3, 3);
    }

    private void ApplyMainGuardian(UI_MainGuardianPanel guardian, int index)
    {
        if (guardian == null) return;
        float ratio = GuardRatio(index, out int health, out int max);
        if (guardian.m_Name != null) guardian.m_Name.text = GlobalConfig.GuardPointConfig(index).name;
        if (guardian.m_health_label != null) guardian.m_health_label.text = $"{health}/{max}";
        if (guardian.m_health_percentage != null) guardian.m_health_percentage.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
        // 守护层数 = 还没被打掉的副点数量
        if (guardian.m_protection_layer_count != null) guardian.m_protection_layer_count.text = AliveSubCount().ToString();
        SetGuardianBarFill(guardian.m_bar, ratio);
    }

    private void ApplySubGuardian(UI_SubGuardianPanel guardian, int index)
    {
        if (guardian == null) return;
        float ratio = GuardRatio(index, out _, out _);
        if (guardian.m_guardian_name != null) guardian.m_guardian_name.text = GlobalConfig.GuardPointConfig(index).name;
        if (guardian.m_health_percentage != null) guardian.m_health_percentage.text = $"{Mathf.RoundToInt(ratio * 100f)}%";
        if (guardian.m_killed != null) guardian.m_killed.selectedIndex = ratio <= 0f ? 1 : 0;
        SetGuardianBarFill(guardian.m_bar, ratio);
    }

    // 血量未知（守护点还没生成）按满血算，避免开局显示成空槽
    private float GuardRatio(int index, out int health, out int max)
    {
        max = Mathf.Max(1, Mathf.RoundToInt(GlobalConfig.GuardPointConfig(index).health));
        health = guardianHealth[index] < 0 ? max : guardianHealth[index];
        return Mathf.Clamp01((float)health / max);
    }

    private int AliveSubCount()
    {
        int alive = 0;
        for (int i = 1; i < guardianHealth.Length; i++)
            if (guardianHealth[i] != 0) alive++; // 0 = 已摧毁
        return alive;
    }

    // 守护点血条没有 fillAmount，按横向缩放裁填充图（FGUI 里填充图没命名）
    private static void SetGuardianBarFill(GComponent bar, float ratio)
    {
        if (bar == null) return;
        var fill = bar.GetChild("n0");
        if (fill != null) fill.scale = new Vector2(ratio, fill.scale.y);
    }
    #endregion

    private void UpdateLocalPlayerBar(SCEntityDisplayInfo info)
    {
        var playerPanel = panel.m_playerPanel;
        if (playerPanel == null) return;
        var vitals = ResolveVitals(info);
        if (playerPanel.m_bar_health != null && playerPanel.m_bar_health.m_fill != null)
            playerPanel.m_bar_health.m_fill.fillAmount = vitals.maxHealth > 0 ? Mathf.Clamp01((float)vitals.health / vitals.maxHealth) : 0f;
        if (playerPanel.m_label_health != null) playerPanel.m_label_health.text = $"{vitals.health}/{vitals.maxHealth}";
        if (info.hasBuffs) RefreshBuffList(playerPanel.m_buffList.m_list, localBuffs, info.buffs);
    }

    private void OnScoreUpdate(SCScoreInfo info)
    {
        if (info == null) return;
        if (panel.m_BattleInfoPanel.m_time_left != null) panel.m_BattleInfoPanel.m_time_left.text = FormatTime(Mathf.Max(0f, info.remainTime));
    }

    private void OnCampAliveCount(SCCampAliveCount info) => RefreshCampAliveCount(info);

    // 三方阵营信息：每行自带 c1 控制器（0 攻 / 1 守 / 2 观战），是不是自己阵营交给 is_in_tis_camp
    private void RefreshCampAliveCount(SCCampAliveCount info)
    {
        if (info == null) return;
        var camps = panel.m_CampInfoPanel;
        if (camps == null) return;
        ApplyCampCount(camps.m_camp_attack, info.attackerAlive);
        ApplyCampCount(camps.m_camp_defense, info.defenseAlive);
        ApplyCampCount(camps.m_camp_spectate, info.spectatorCount);
    }

    private void ApplyCampCount(UI_CampInfo row, int count)
    {
        if (row == null) return;
        if (row.m_member_count != null) row.m_member_count.text = count.ToString();
        if (row.m_is_in_tis_camp != null)
            row.m_is_in_tis_camp.selectedIndex = row.m_c1 != null && row.m_c1.selectedIndex == localPlayerCamp ? 1 : 0;
    }

    private void OnMinimapUpdate(SCMinimapEntity e)
    {
        if (e == null) return;
        // 每个实体一个独立包：只登记坐标与类别，换算和显隐交给小地图自身
        panel.m_Minimap?.Push(new UI_Minimap.Point(e.entityId, e.type.category, e.type, e.camp,
            e.marked, IsOwnEntity(e.entityId), new Vector2(e.posX, e.posZ)), (EntityCamp)localPlayerCamp);
    }

    //F 键切换雷达显示半径：服务器权威回应，客户端换用新半径做裁剪与缩放（点位下一帧即按新半径重排）
    private void OnMinimapRadiusUpdate(float radius)
    {
        panel.m_Minimap?.SetRadius(radius);
    }

    // 圆心与自身朝向的取数在这边（相机/会话/实体表现都在战斗页的依赖里），换算与绘制全在小地图自身
    private void TickMinimap()
    {
        bool hasCenter = TryGetMinimapCenter(out var center);
        bool hasYaw = TryGetSelfYaw(out float yaw);
        panel.m_Minimap?.Tick(center, hasCenter, yaw, hasYaw);
    }

    #region 小地图展开 / 上帝视角
    private void TickMinimapPanels(float deltaTime)
    {
        if (panel.m_largeMinimapPanel.IsOpen)
        {
            bool hasYaw = TryGetSelfYaw(out float yaw);
            var points = panel.m_Minimap != null ? panel.m_Minimap.CurrentPoints : null;
            panel.m_largeMinimapPanel.Refresh(points, (EntityCamp)localPlayerCamp, yaw, hasYaw);
        }
        panel.m_godViewPanel.Tick(deltaTime, (EntityCamp)localPlayerCamp, OwnEntityId);
    }

    // 小地图：点小地图组件上的按钮打开，关闭走底框关闭按钮
    private void OpenLargeMinimap()
    {
        if (!panel.m_largeMinimapPanel.IsOpen) panel.m_largeMinimapPanel.Open();
    }

    // 上帝视角：窗口期内连按 3 次 G 打开（与 GM 面板同一套连按判定），关闭走底框关闭按钮
    private void TickGodViewOpenKey()
    {
        if (panel.m_godViewPanel.IsOpen)
        {
            ResetGodViewPress(); // 已打开时不再累计，避免关闭后残留一半计数
            return;
        }
        if (!Input.GetKeyDown(GlobalConfig.god_view_open_key)) return;
        if (godViewPressCount == 0 || Time.unscaledTime - godViewFirstPressTime > GlobalConfig.god_view_open_press_window)
        {
            godViewPressCount = 1;
            godViewFirstPressTime = Time.unscaledTime;
        }
        else godViewPressCount++;
        if (godViewPressCount < GlobalConfig.god_view_open_press_count) return;
        ResetGodViewPress();
        panel.m_godViewPanel.Open(ResolveGodViewCenter());
    }

    private void ResetGodViewPress()
    {
        godViewPressCount = 0;
        godViewFirstPressTime = 0f;
    }

    // 初始区域中心 = 雷达圆心（本地实体 / 观战目标 / 自由视角相机），取不到就从地图正中开始
    private Vector2 ResolveGodViewCenter()
    {
        if (TryGetMinimapCenter(out var center)) return new Vector2(center.x, center.z);
        float half = GlobalVariable.RangeLength(global_variablesEnum.terrain_size) * 0.5f;
        return new Vector2(half, half);
    }

    private void OnGodViewEntities(SCGodViewEntities chunk) => panel.m_godViewPanel.OnEntities(chunk);

    private void OnGodViewCorals(SCGodViewCorals chunk) => panel.m_godViewPanel.OnCorals(chunk);

    private void OnGodViewDetail(SCGodViewEntityDetail info) => panel.m_godViewPanel.OnDetail(info);
    #endregion

    // 自身图标随朝向旋转：地图不转，图标转（每帧读本地玩家推演朝向）
    private bool TryGetSelfYaw(out float yaw)
    {
        yaw = 0f;
        if (!HasSession || Tool.ClientLogicManager == null
            || Tool.ClientLogicManager.EntityPlayers == null) return false;
        if (!Tool.ClientLogicManager.EntityPlayers.TryGetEntityTransform(OwnEntityId, out _, out var rot)) return false;
        yaw = rot.eulerAngles.y; //FGUI 顺时针角度与 Unity yaw 同向（图标默认朝上时）
        return true;
    }

    // 雷达中心 = 本地玩家实体位置；纯观战无本地实体时取观战目标位置；自由视角下取相机自身
    private bool TryGetMinimapCenter(out Vector3 pos)
    {
        pos = Vector3.zero;
        // 自由视角相机已脱离跟随点：圆心必须跟相机，否则服务器按相机下发的点位会被按旧圆心换算而漂出雷达
        var cam = Tool.CameraController;
        if (cam != null && cam.IsFree)
        {
            pos = cam.transform.position;
            return true;
        }
        if (!HasSession || Tool.ClientLogicManager == null
            || Tool.ClientLogicManager.EntityPlayers == null) return false;
        ushort centerId = OwnEntityId;
        if (centerId == 0 && spectateTargetEntityId > 0) centerId = (ushort)spectateTargetEntityId;
        return centerId != 0 && Tool.ClientLogicManager.EntityPlayers.TryGetEntityPosition(centerId, out pos);
    }

    // EventBanner.m_type 控制器页序：0 天亮 / 1 天黑
    private const int BannerDaybreak = 0, BannerNightfall = 1;

    // 切 EventBanner 档位并播 BattlePanel 的显隐转场
    private void ShowEventBanner(int page)
    {
        var banner = panel.m_eventBanner;
        if (banner == null || banner.m_type == null) return;
        banner.m_type.selectedIndex = page;
        if (panel.m_showBanner != null) panel.m_showBanner.Play();
    }

    // 重要事件 banner：技能名取 skill.csv，播报文案格式固定
    private void EnqueueImportantBanner(int skillId)
    {
        importantBannerQueue.Enqueue(ImportantBannerPrefix + SkillConfig.Get(skillId).name);
        PlayNextImportantBanner();
    }

    // 一条播完（m_t0 结束回调）再取下一条；队列空则收起
    private void PlayNextImportantBanner()
    {
        var banner = panel.m_important_event_banner;
        if (banner == null || importantBannerPlaying) return;
        if (importantBannerQueue.Count == 0)
        {
            banner.visible = false;
            return;
        }
        importantBannerPlaying = true;
        banner.visible = true;
        if (banner.m_title != null) banner.m_title.text = importantBannerQueue.Dequeue();
        if (banner.m_t0 != null) banner.m_t0.Play(OnImportantBannerPlayed);
        else OnImportantBannerPlayed();
    }

    private void OnImportantBannerPlayed()
    {
        importantBannerPlaying = false;
        PlayNextImportantBanner();
    }

    // 进/退出战斗都收起，并丢掉未播的积压
    private void HideImportantBanner()
    {
        importantBannerQueue.Clear();
        importantBannerPlaying = false;
        var banner = panel.m_important_event_banner;
        if (banner == null) return;
        if (banner.m_t0 != null) banner.m_t0.Stop();
        banner.visible = false;
    }

    // 事件条目播报在聊天面板（ChatPlugin 系统消息页签），这里只负责日夜大横幅与技能播报 banner
    private void OnBattleEvent(SCBattleEvent e)
    {
        if (e == null) return;
        if (e.type == SCBattleEvent.Type.Nightfall) ShowEventBanner(BannerNightfall);     // 天黑横幅
        else if (e.type == SCBattleEvent.Type.Daybreak) ShowEventBanner(BannerDaybreak);  // 天亮横幅
        else if (e.type == SCBattleEvent.Type.SkillBroadcast) EnqueueImportantBanner(e.textId); // 特殊技能播报
    }

    private void OnCombatLabelDisplay(SCCombatLabel label)
    {
        if (label == null) return;
        var type = (CombatLabelType)label.type;
        var targetId = (ushort)label.targetId;
        ShowCombatLabel(label.amount, type, targetId, label.hasHitPos, label.hitPos);
        PlayHitVfx(type, targetId, label.hasHitPos, label.hitPos);
        if (IsOwnEntity(targetId)) PlaySelfHitAnim(type);
    }

    // 复活条只服务本地玩家：包按令牌单播，观战目标死亡不驱动本界面
    private void OnReviveProgressUpdate(SCReviveInfo info)
    {
        if (info == null || !HasSession) return;

        if (info.ready)
        {
            reviveActive = false;
            panel.m_showRegenerationBar.selectedIndex = 0;
            return;
        }

        // 只拿死亡次数：复活总时长由客户端读配表算，progress 只作为起点（重连补发时非 0）
        var revive = ReviveTimeConfig.GetClamped(Mathf.Max(1, info.deathCount));
        bool isAttack = Session.Camp == EntityCamp.Attack;
        reviveDuration = isAttack ? revive.attackReviveSeconds : revive.defenseReviveSeconds;
        reviveElapsed = Mathf.Clamp01(info.progress) * reviveDuration;
        reviveActive = true;
        panel.m_showRegenerationBar.selectedIndex = 1;
        RefreshReviveBar();
    }

    private void TickReviveProgress(float deltaTime)
    {
        if (!reviveActive) return;
        reviveElapsed += deltaTime;
        RefreshReviveBar();
    }

    private void RefreshReviveBar()
    {
        if (panel.m_regeneration_progressbar != null)
            panel.m_regeneration_progressbar.fillAmount = Mathf.Clamp01(reviveElapsed / reviveDuration);
    }
    #endregion

    #region//Local
    private void UpdateEntityBar(SCEntityDisplayInfo info, bool withName)
    {
        if (!entityBars.TryGetValue(info.entityId, out var bar))
        {
            bar = UI_EntityBar.CreateInstance();
            panel.m_world_element_container.AddChild(bar); // 挂在世界层：压在战斗界面各元素之下
            entityBars[info.entityId] = bar;
        }
        string name = withName && HasSession ? Session.GetNameByEntity(info.entityId) : "";
        var vitals = ResolveVitals(info);
        bar.Bind(vitals.health, vitals.maxHealth, info.camp, (EntityCamp)localPlayerCamp, name,
            info.hasBuffs ? ToBarBuffs(info.buffs) : null, ShowBuffTip, HideBuffTip);
    }

    // 血量段只在置脏/全量包携带：带则刷新缓存，不带则沿用上次值（首见包必为全量）
    private (int health, int maxHealth) ResolveVitals(SCEntityDisplayInfo info)
    {
        if (info.hasHealth) entityVitals[info.entityId] = (info.health, info.maxHealth);
        return entityVitals.TryGetValue(info.entityId, out var vitals) ? vitals : default;
    }

    // 协议 buff 列表 → 血条条目数据（复用缓冲，避免每个摘要包都分配）
    private IReadOnlyList<UI_EntityBar.Buff> ToBarBuffs(List<SCEntityDisplayInfo.BuffRuntime> source)
    {
        barBuffs.Clear();
        if (source != null)
            for (int i = 0; i < source.Count; i++)
                if (source[i] != null) barBuffs.Add(new UI_EntityBar.Buff(source[i].type, source[i].level));
        return barBuffs;
    }

    // 本地玩家血条的 buff 列表：协议数据拷进缓冲后按条数重建
    private static void RefreshBuffList(GList list, List<SCEntityDisplayInfo.BuffRuntime> target, List<SCEntityDisplayInfo.BuffRuntime> source)
    {
        target.Clear();
        if (source != null) target.AddRange(source);
        list.numItems = target.Count;
    }

    // 头顶锚点由实体表现提供，投影与缩放归血条自身
    private void UpdateEntityBarPositions()
    {
        if (entityBars.Count == 0) return;
        foreach (var pair in entityBars)
        {
            var bar = pair.Value;
            if (bar == null) continue;
            bool hasAnchor = false;
            var headPos = Vector3.zero;
            if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.EntityPlayers != null)
                hasAnchor = Tool.ClientLogicManager.EntityPlayers.TryGetAnchorPos(pair.Key, EntityAnchor.Bar, out headPos);
            bar.Place(hasAnchor, headPos, barDefaultSizeDistance);
        }
    }

    private void RemoveEntityBar(ushort entityId)
    {
        if (entityBars.TryGetValue(entityId, out var bar))
        {
            if (bar != null) bar.Dispose();
            entityBars.Remove(entityId);
        }
    }

    private void ClearEntityBars()
    {
        foreach (var bar in entityBars.Values)
        {
            if (bar != null) bar.Dispose();
        }
        entityBars.Clear();
    }

    private void ShowCombatLabel(int value, CombatLabelType type, ushort targetId, bool hasHitPos, Vector3 hitPos)
    {
        // 锚点：有命中点用命中点，否则退到实体头顶（飘字与命中特效共用这套兜底）
        Vector3 anchor = hitPos;
        if (!hasHitPos)
        {
            anchor = Vector3.zero;
            if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.EntityPlayers != null)
                Tool.ClientLogicManager.EntityPlayers.TryGetAnchorPos(targetId, EntityAnchor.LabelFallBack, out anchor);
        }
        UI_DamageLabel.Spawn(panel.m_world_element_container, value, type, anchor, labelDefaultSizeDistance);
    }

    // 命中特效：直接伤害取命中点高度 + 实体所在列，buff/DoT 类贴实体中心；闪避与失效没有命中不播
    private void PlayHitVfx(CombatLabelType type, ushort targetId, bool hasHitPos, Vector3 hitPos)
    {
        if (type == CombatLabelType.Evade || type == CombatLabelType.Invalid) return;
        if (Tool.VfxManager == null || Tool.ClientLogicManager == null || Tool.ClientLogicManager.EntityPlayers == null) return;
        var players = Tool.ClientLogicManager.EntityPlayers;

        if (type == CombatLabelType.Normal || type == CombatLabelType.Crit || type == CombatLabelType.Shield)
        {
            if (!players.TryGetEntityPosition(targetId, out var entityPos)) return; // 视图未建/已移除：飘字兜底逻辑不变，特效不播
            float y;
            if (hasHitPos) y = hitPos.y;
            else if (players.TryGetAnchorPos(targetId, EntityAnchor.Center, out var center)) y = center.y;
            else return;
            Tool.VfxManager.PlayHitVFX(HitVfxIndexDirect, new Vector3(entityPos.x, y, entityPos.z), HitVfxLife);
        }
        else
        {
            if (!players.TryGetAnchorPos(targetId, EntityAnchor.Center, out var center)) return;
            Tool.VfxManager.PlayHitVFX(HitVfxIndexBuff, center, HitVfxLife);
        }
    }

    // 自己被打时的受击动画：只有命中伤害（正常/暴击/护盾）播，DoT 与其它结算不播；连击时从头重播
    private void PlaySelfHitAnim(CombatLabelType type)
    {
        if (type != CombatLabelType.Normal && type != CombatLabelType.Crit && type != CombatLabelType.Shield) return;
        if (panel.m_showHit != null) panel.m_showHit.Play();
    }

    // 相机插值收尾回调：用最终相机变换重投影血条/名字/飘字，消除与渲染差一帧的震颤
    private void OnCameraUpdated() => UpdateEntityBarPositions();

    private void RefreshTimeIcon()
    {
        var dayNight = panel.m_BattleInfoPanel != null ? panel.m_BattleInfoPanel.m_display_day_night : null;
        if (dayNight == null || dayNight.m_pointer == null) return;
        dayNight.m_pointer.rotation = 180f - EnvironmentManager.Time01 * 180f;
    }

    private static string FormatTime(float seconds)
    {
        int s = Mathf.CeilToInt(seconds);
        return $"{s / 60:D2}:{s % 60:D2}";
    }

    #region 悬浮提示
    private void ShowSkillTip(int skillId, GObject anchor)
    {
        HideBuffTip();
        if (skillTip == null) skillTip = UI_SkillTip.CreateInstance();
        if (skillTip.parent == null) Root.AddChild(skillTip);
        skillTip.Fill(skillId);
        skillTip.ShowAt(anchor);
    }

    private void ShowBuffTip(int buffType, GObject anchor)
    {
        HideSkillTip();
        if (buffTip == null) buffTip = UI_BuffTip.CreateInstance();
        if (buffTip.parent == null) Root.AddChild(buffTip);
        buffTip.Fill(buffType);
        buffTip.ShowAt(anchor);
    }

    private void HideSkillTip() => skillTip?.Hide();

    private void HideBuffTip() => buffTip?.Hide();

    private void HideAllTips()
    {
        HideSkillTip();
        HideBuffTip();
    }
    #endregion

    // 退出确认：确认后 ExitWorld → NetworkManager 触发 OnExitWorld → UIManager 切回主界面
    private void OnExitClicked()
    {
        if (Tool.UIManager != null) Tool.UIManager.ShowConfirm("退出将放弃本局，确定退出吗？", DoExit);
        else DoExit();
    }

    private static void DoExit()
    {
        if (Tool.NetworkManager != null) Tool.NetworkManager.ExitWorld();
    }
    #endregion

    #region 抽卡（悬浮按钮 → 抽卡界面 → 选/弃）
    private readonly List<int> pendingTiers = new();   // 有次数的档位（渲染用，避免每帧分配）
    private readonly int[] cardPending = new int[CardCatalog.TierCount];
    private int cardLuck;             // 幸运：只影响档位内能刷出多深的卡
    private int cardLevel = 1;        // 局内等级（左上角等级 + 经验条）
    private int cardExp;              // 当前等级内已积累的经验
    private CardTier currentCardTier; // 最近一次打开的档位（弃卡按它上报）

    // 服务器只下发「幸运 + 每档有多少次」，抽到什么在这里算
    private void OnPlayerProgress(SCPlayerProgress info)
    {
        cardLuck = info.luck;
        cardLevel = info.level;
        cardExp = info.exp;
        cardPending[0] = info.silver;
        cardPending[1] = info.gold;
        cardPending[2] = info.holo;

        // 面板开着但这一档已经被抽完（例如弃卡后服务器结算）→ 收起，避免显示过期卡面
        if (panel.m_selectCardPanel.IsOpen && cardPending[(int)panel.m_selectCardPanel.OpenedTier] <= 0)
            panel.m_selectCardPanel.CloseImmediately();

        RefreshCardButtons();
        RefreshProgressHud();
    }

    // 左上角等级 / 经验条：等级上限取自 character_upgrade_exp.csv，满级显示 MAX
    private void RefreshProgressHud()
    {
        var playerPanel = panel.m_playerPanel;
        if (playerPanel == null) return;
        int need = CharacterExpConfig.NeedExp(cardLevel + 1);
        if (levelLabel != null) levelLabel.text = cardLevel.ToString();
        if (playerPanel.m_bar_exp != null && playerPanel.m_bar_exp.m_fill != null)
            playerPanel.m_bar_exp.m_fill.fillAmount = need > 0 ? Mathf.Clamp01((float)cardExp / need) : 1f;
        if (playerPanel.m_label_exp != null) playerPanel.m_label_exp.text = need > 0 ? $"{cardExp}/{need}" : "MAX";
    }

    private void RefreshCardButtons()
    {
        pendingTiers.Clear();
        for (int tier = 0; tier < cardPending.Length; tier++)
        {
            if (cardPending[tier] > 0) pendingTiers.Add(tier);
        }
        if (panel.m_list_selectCard != null) panel.m_list_selectCard.numItems = pendingTiers.Count;
    }

    private void RenderCardButton(int index, GObject obj)
    {
        if (obj is not UI_btn_SelectCard btn || index >= pendingTiers.Count) return;
        int tier = pendingTiers[index];
        if (btn.m_level != null) btn.m_level.selectedIndex = tier;
        if (btn.m_label_num != null) btn.m_label_num.text = $"×{cardPending[tier]}";
        btn.onClick.Set(() => OpenCardPanel((CardTier)tier));
    }

    // 点悬浮按钮：随机抽 3 张（排除已持有技能），交给抽卡界面展示
    private void OpenCardPanel(CardTier tier)
    {
        if (cardPending[(int)tier] <= 0) return;
        var held = Skills != null ? new List<int>(Skills.HeldSkills) : null;
        var rolls = CardCatalog.Roll(tier, cardLuck, CardCampForRoll(), 3, held);
        if (rolls.Count == 0)
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText("该档暂无可抽的卡");
            return;
        }
        currentCardTier = tier;
        panel.m_selectCardPanel.Open(rolls, tier, cardLuck);
    }

    // card_option.csv 的阵营限制：0 = 进攻方，1 = 防守方
    private int CardCampForRoll() => localPlayerCamp == (int)EntityCamp.Defense ? 1 : 0;

    private void OnCardSelected(CardRoll roll)
    {
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendSelectCard(new CSSelectCard
        {
            tier = (byte)roll.tier,
            cardId = roll.cardId,
            skillId = roll.IsSkill ? roll.skillId : -1,
        });
    }

    private void OnCardDiscarded()
    {
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendDiscardCard(new CSDiscardCard { tier = (byte)currentCardTier });
    }

    private void ResetCardState()
    {
        for (int i = 0; i < cardPending.Length; i++) cardPending[i] = 0;
        pendingTiers.Clear();
        cardLuck = 0;
        cardLevel = 1;
        cardExp = 0;
        currentCardTier = CardTier.Silver;
        RefreshProgressHud();
        if (panel.m_list_selectCard != null) panel.m_list_selectCard.numItems = 0;
    }
    #endregion
}
