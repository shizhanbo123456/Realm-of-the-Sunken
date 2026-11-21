using System.Collections.Generic;
using System.Threading.Tasks;
using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

using Ros.Config;
// 首页：攻/守分页角色列表 + 属性面板 + 改名弹窗 + 连接
public class HomePage : PageBase
{
    private static readonly message_variablesEnum[] StatNameKeys =
    {
        message_variablesEnum.home_attrName_health, message_variablesEnum.home_attrName_attack,
        message_variablesEnum.home_attrName_crit, message_variablesEnum.home_attrName_critDamage,
        message_variablesEnum.home_attrName_view, message_variablesEnum.home_attrName_skillSlot,
    };
    private static readonly message_variablesEnum[] StatSuffixKeys = // 与 StatNameKeys 一一对应
    {
        message_variablesEnum.home_attrSuffixes_health, message_variablesEnum.home_attrSuffixes_attack,
        message_variablesEnum.home_attrSuffixes_crit, message_variablesEnum.home_attrSuffixes_critDamage,
        message_variablesEnum.home_attrSuffixes_view, message_variablesEnum.home_attrSuffixes_skillSlot,
    };
    private const int AttrBarSegments = 10; // 属性条格数（UI_AttrItem.m_progressBar 里的 UI_AttrValueItem 数量）
    private const int AttrIndexHealth = 0;         // 属性列表里生命的下标
    private const int RangeKeyHealthAttack = 6;    // attr_range：进攻方生命（两阵营血量量级不同，分开取区间）
    private const int RangeKeyHealthDefense = 7;   // attr_range：防守方生命

    private readonly UI_HomePanel panel;
    private UI_RemanePanel renamePanel; // 改名弹窗：设计自带全屏遮罩，挂 GRoot 置顶
    private bool aiBound;               // 本页的 AI 命令是否已注册
    private bool ipRestored;            // 本次运行是否已把存档里的服务器地址填进输入框

    public HomePage(UI_HomePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.home;

    protected override void Init()
    {
        var selection = panel.m_character_selection_panel;
        selection.m_characterList.itemRenderer = RenderCharacter;
        selection.m_tab_attacker.onClick.Add(() => SwitchCamp(false));
        selection.m_tab_defenser.onClick.Add(() => SwitchCamp(true));

        // 玩家改名：按钮与名字都可点
        panel.m_playerInfo.m_label_playerName.touchable = true;
        panel.m_playerInfo.m_label_playerName.onClick.Add(OpenRename);
        panel.m_playerInfo.m_btn_editName.onClick.Add(OpenRename);
        BuildRenamePanel();

        panel.m_character_attr_panel.m_btn_select_character.onClick.Add(ConfirmSelection);

        panel.m_btn_settings.onClick.Add(OpenSettings);

        panel.m_connectPanel.m_btn_connect.title = MessageVariable.V(message_variablesEnum.home_btnConnect);
        panel.m_connectPanel.m_btn_connect.onClick.Add(OnConnectClicked);
        // 输入框改一次记一次：不点连接也要把地址记进存档（落盘仍在退出时）
        panel.m_connectPanel.m_input_ipaddress.m_content.onChanged.Add(OnServerIpChanged);
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        Tool.EnvironmentManager.SetTime(GlobalConfig.home_env_time); // 固定首页昼夜，不沿用对局结束时的时刻
        // 每次进首页固定从进攻方已选角色开始展示，不沿用上次浏览到的角色
        defenseTab = false;
        selectedSaveIndex = ClientSelection.selectedAttackIndex;
        RefreshPlayerName();
        Refresh();
        RefreshPreview();
        if (renamePanel != null) renamePanel.visible = false;
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetHomeView();

        RestoreServerIp();
        BindAiCommands();
    }

    // 存档里的服务器地址只在本次运行首次进首页时回填，之后不覆盖玩家正在输入的内容
    private void RestoreServerIp()
    {
        if (ipRestored) return;
        ipRestored = true;
        if (Tool.SaveManager == null)
            throw new System.InvalidOperationException("[首页] 场景缺少 SaveManager，无法读取服务器地址");
        panel.m_connectPanel.m_input_ipaddress.m_content.text = Tool.SaveManager.lastServerIp;
    }

    // 输入框的每次改动都同步进存档字段，避免「只改地址不点连接」时地址丢失
    private void OnServerIpChanged()
    {
        if (Tool.SaveManager == null)
            throw new System.InvalidOperationException("[首页] 场景缺少 SaveManager，无法记录服务器地址");
        Tool.SaveManager.lastServerIp = panel.m_connectPanel.m_input_ipaddress.m_content.text;
    }

    public override void Exit()
    {
        base.Exit();
        if (renamePanel != null) renamePanel.visible = false; // 弹窗挂 GRoot，不收起来会留在别的页面上
        ResignAiCommands();
    }

    public override void Shutdown() => ResignAiCommands();

    // 首页这个阶段只对 AI 开放这四条命令，离开首页就摘掉
    private void BindAiCommands()
    {
        if (aiBound) return;
        aiBound = true;
        AiCommandBus.Bind(ai_commandEnum.home_setPlayerName, OnAiSetPlayerName);
        AiCommandBus.Bind(ai_commandEnum.home_selectCharacter, OnAiSelectCharacter);
        AiCommandBus.Bind(ai_commandEnum.home_connect, OnAiConnect);
        AiCommandBus.Bind(ai_commandEnum.takeover, OnAiTakeover);
    }

    private void ResignAiCommands()
    {
        if (!aiBound) return;
        aiBound = false;
        AiCommandBus.Unbind(ai_commandEnum.home_setPlayerName, OnAiSetPlayerName);
        AiCommandBus.Unbind(ai_commandEnum.home_selectCharacter, OnAiSelectCharacter);
        AiCommandBus.Unbind(ai_commandEnum.home_connect, OnAiConnect);
        AiCommandBus.Unbind(ai_commandEnum.takeover, OnAiTakeover);
    }

    #region AI 命令（与玩家点界面走同一条链路）
    private void OnAiSetPlayerName(AiRequest request)
    {
        if (!TrySetPlayerName(request.Str(0), out int min, out int max))
        {
            request.Reject(string.Format(PromptConfig.Get(promptEnum.name_length_invalid).text, min, max));
            return;
        }
    }

    private void OnAiSelectCharacter(AiRequest request)
    {
        int camp = request.Int(0);
        int index = request.Int(1);
        if (camp != 0 && camp != 1)
        {
            request.Reject("camp 只能是 0 进攻方 / 1 防守方");
            return;
        }

        bool defense = camp == 1;
        int count = GlobalConfig.attack_character_count;
        if (defense) count = GlobalConfig.defense_character_count;
        if (index < 0 || index >= count)
        {
            string campName = "进攻";
            if (defense) campName = "防守";
            request.Reject($"{campName}方下标范围 0~{count - 1}");
            return;
        }

        SelectCharacter(defense, index); // 与「确认选择」同一个落盘入口：切页签、浏览、刷新预览、写 ClientSelection
    }

    private void OnAiConnect(AiRequest request)
    {
        // 命令行连接必须先接管：aiTakeover 随连接上报，连上后改不了
        if (!InputManager.IsAiControl)
        {
            request.Reply("必须先接管游戏才能用命令行参与游戏");
            return;
        }

        // 与连接按钮同一条前置：名字不合法不许连
        if (!TrySetPlayerName(ClientSelection.playerName, out int min, out int max))
        {
            request.Reject(string.Format(PromptConfig.Get(promptEnum.name_length_invalid).text, min, max));
            return;
        }

        ConnectAndReport(request.Str(0));
    }

    // 连接是异步的：结果以飘字落地，玩家与 AI 看到的是同一条
    private async void ConnectAndReport(string ip)
    {
        try
        {
            var result = await ConnectAsync(ip);
            if (Tool.UIManager == null) return;
            if (result == NetworkManager.ConnectResult.Success) Tool.UIManager.ShowFlyText(promptEnum.connect_success);
            else Tool.UIManager.ShowFlyText(promptEnum.connect_failed);
        }
        catch (System.Exception e)
        {
            Debug.LogError("[AI] 连接异常：" + e.Message);
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.connect_failed);
        }
    }

    private void OnAiTakeover(AiRequest request)
    {
        bool on = request.Bool(0);
        if (on) InputManager.SetControlMode(PlayerControlMode.Ai);
        else InputManager.SetControlMode(PlayerControlMode.Player);
    }
    #endregion

    public override void OnResize(float width, float height)
    {
        base.OnResize(width, height);
        ApplyRenameTransform();
    }

    #region//Local
    private void BuildRenamePanel()
    {
        renamePanel = UI_RemanePanel.CreateInstance();
        renamePanel.opaque = true;   // 吞掉点击：弹窗开着时首页与连接按钮都不响应
        renamePanel.touchable = true;
        GRoot.inst.AddChild(renamePanel);
        renamePanel.visible = false;

        renamePanel.m_input.maxLength = GlobalVariable.I(global_variablesEnum.name_length_max);
        renamePanel.m_confirm.onClick.Add(CommitRename);
        renamePanel.m_panel.m_close.onClick.Set(CloseRename); // 关闭 / 取消 = 放弃修改
        renamePanel.m_cancel.onClick.Set(CloseRename);
    }

    private void OpenRename()
    {
        renamePanel.m_input.text = ClientSelection.playerName;
        GRoot.inst.AddChild(renamePanel); // 每次弹出都重新置顶
        renamePanel.visible = true;
        ApplyRenameTransform();
    }

    private void CloseRename() => renamePanel.visible = false;

    // 玩家名唯一的落盘入口：改名弹窗、连接前校验、AI 的改名命令都走这里
    // 返回 false 表示长度不合法，min/max 是配表区间，供调用方提示
    private bool TrySetPlayerName(string rawName, out int min, out int max)
    {
        min = GlobalVariable.I(global_variablesEnum.name_length_min);
        max = GlobalVariable.I(global_variablesEnum.name_length_max);
        string name = rawName;
        if (name == null) name = "";
        name = name.Trim();
        if (name.Length < min || name.Length > max) return false;

        ClientSelection.playerName = name;
        if (Tool.SaveManager != null) Tool.SaveManager.playerName = name;
        RefreshPlayerName();
        return true;
    }

    // 只有名字合法才改名并关闭；不合法只飘字，弹窗留在原地
    private void CommitRename()
    {
        if (!TrySetPlayerName(renamePanel.m_input.text, out int min, out int max))
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.name_length_invalid, min, max);
            return;
        }
        renamePanel.visible = false;
    }

    private void RefreshPlayerName()
    {
        panel.m_playerInfo.m_label_playerName.text = ClientSelection.playerName;
    }

    // GM 换临时凭证后就地刷新：新名字与回零的选角都不必退回首页重进
    public void RefreshCredential()
    {
        defenseTab = false;
        selectedSaveIndex = ClientSelection.selectedAttackIndex;
        RefreshPlayerName();
        Refresh();
        RefreshPreview();
    }

    // 与页面同比例缩放并居中（弹窗尺寸 400×300，自带全屏遮罩）
    private void ApplyRenameTransform()
    {
        if (renamePanel == null) return;
        renamePanel.scale = new Vector2(UiScale, UiScale);
        renamePanel.pivotAsAnchor = true;
        renamePanel.pivot = new Vector2(0.5f, 0.5f);
        renamePanel.SetXY(GRoot.inst.width * 0.5f, GRoot.inst.height * 0.5f);
    }

    // 设置面板是全局子界面，三个页面共用（UIManager 持有）
    private static void OpenSettings()
    {
        if (Tool.UIManager != null) Tool.UIManager.OpenSettings();
    }

    // 存档下标：0~5 进攻、6~11 防守；页签只列其中一侧
    private static bool IsDefense(int saveIndex) => saveIndex >= GlobalConfig.attack_character_count;

    private bool defenseTab;

    // 当前浏览的角色（仅本页内存状态，不进存档、不进 ClientSelection）
    private int selectedSaveIndex;

    private int CampOffset
    {
        get
        {
            if (defenseTab) return GlobalConfig.attack_character_count;
            return 0;
        }
    }

    // 本阵营已选角色的存档下标
    private int ChosenSaveIndex
    {
        get
        {
            int selected = ClientSelection.selectedAttackIndex;
            if (defenseTab) selected = ClientSelection.selectedDefenseIndex;
            return CampOffset + selected;
        }
    }

    private void Refresh()
    {
        var selection = panel.m_character_selection_panel;
        int count = GlobalConfig.attack_character_count;
        if (defenseTab) count = GlobalConfig.defense_character_count;
        if (defenseTab) selection.m_current_selected_camp.selectedIndex = 1;
        else selection.m_current_selected_camp.selectedIndex = 0;
        selection.m_tab_attacker.selected = !defenseTab;
        selection.m_tab_defenser.selected = defenseTab;
        selection.m_characterList.numItems = count;
        // numItems 不变时 FGUI 不重绘已有条目，切页签与选中变化都要手动重画
        for (int i = 0; i < count; i++) RenderCharacter(i, selection.m_characterList.GetChildAt(i));

        int selectedMode = 0;
        if (selectedSaveIndex == ChosenSaveIndex) selectedMode = 1;
        panel.m_character_attr_panel.m_character_selcted.selectedIndex = selectedMode;
        RefreshAttrList();
        RefreshSpecialAbilities();
        RefreshCharacterInfo();
    }

    // 切阵营后回到该阵营已选角色
    private void SwitchCamp(bool defense)
    {
        if (defenseTab == defense) return;
        defenseTab = defense;
        Browse(ChosenSaveIndex);
    }

    // 特殊能力：初始技能 + 被动描述，均读配表（见 GuideCatalog）
    private void RefreshSpecialAbilities()
    {
        int saveIndex = selectedSaveIndex;
        entity info = GetCharacterInfo(saveIndex);
        var entries = GuideCatalog.BuildCharacterAbilities(info);

        var list = panel.m_character_attr_panel.m_special_ability_list;
        list.itemRenderer = (i, obj) =>
        {
            var item = (UI_HomeSpecialAbility)obj;
            if (string.IsNullOrEmpty(entries[i].description)) item.m_label.text = entries[i].title;
            else item.m_label.text = $"{entries[i].title}：{entries[i].description}";
        };
        list.numItems = entries.Count;
    }

    private void RenderCharacter(int index, GObject obj)
    {
        var head = (UI_RoleHead)obj;
        int saveIndex = CampOffset + index;
        entity info = GetCharacterInfo(saveIndex);

        head.m_roleName.text = DisplayName(info, saveIndex);

        // 头像：图标列表未导入或下标越界时置空
        List<Texture2D> icons = null;
        if (Tool.ResourceManager != null)
        {
            if (defenseTab) icons = Tool.ResourceManager.DefenseCharacterIcons;
            else icons = Tool.ResourceManager.AttackCharacterIcons;
        }
        bool hasIcon = icons != null && index < icons.Count && icons[index] != null;
        if (hasIcon) head.m_headIcon.texture = new NTexture(icons[index]);
        else head.m_headIcon.texture = null;
        head.m_lock.selectedIndex = 0; // 全角色默认解锁
        int confirmMode = 0;
        if (saveIndex == ChosenSaveIndex) confirmMode = 1;
        head.m_confirm.selectedIndex = confirmMode;

        // 点击浏览（FGUI onClick 为追加制，先清后加以防列表复用叠加）
        head.onClick.Set(() => { });
        head.onClick.Add(() => Browse(saveIndex));
    }

    // 只切换展示与预览，不写出场选择；确认按钮才落 ClientSelection
    private void Browse(int saveIndex)
    {
        selectedSaveIndex = saveIndex;
        Refresh();
        RefreshPreview();
    }

    private void ConfirmSelection()
    {
        int localIndex = selectedSaveIndex - CampOffset;
        if (defenseTab) ClientSelection.selectedDefenseIndex = localIndex;
        else ClientSelection.selectedAttackIndex = localIndex;
        Refresh();
        if (Tool.UIManager != null)
            Tool.UIManager.ShowFlyText(promptEnum.character_selected, panel.m_label_character_name.text);
    }

    // 出场选择唯一的落盘入口：首页「确认选择」按钮与 AI 的选角命令都走这里
    // 先切到该阵营并浏览到该角色（刷新列表 / 属性 / 场景预览），再落 ClientSelection
    private void SelectCharacter(bool defense, int index)
    {
        defenseTab = defense;
        Browse(CampOffset + index);
        ConfirmSelection();
    }

    private void RefreshAttrList()
    {
        int saveIndex = selectedSaveIndex;
        entity info = GetCharacterInfo(saveIndex);
        int[] values = ToValues(info.GetAttribute());
        bool defense = IsDefense(saveIndex);

        panel.m_character_attr_panel.m_attrList.itemRenderer = (i, obj) =>
        {
            var item = (UI_AttrItem)obj;
            item.m_attrName.text = MessageVariable.V(StatNameKeys[i]);
            item.m_attrValue.text = values[i] + MessageVariable.V(StatSuffixKeys[i]);
            FillAttrBar(item.m_progressBar, RangeKey(i, defense), values[i]);
        };
        panel.m_character_attr_panel.m_attrList.numItems = StatNameKeys.Length;
    }

    // 生命按阵营取各自区间，其余属性与 attr_range 行号一致
    private static int RangeKey(int attrIndex, bool defense)
    {
        if (attrIndex != AttrIndexHealth) return attrIndex;
        if (defense) return RangeKeyHealthDefense;
        return RangeKeyHealthAttack;
    }

    // 属性条 = 固定 10 格：按 attr_range.csv 的区间把数值映射成点亮格数（非零至少亮 1 格）
    private static void FillAttrBar(GList bar, int rangeKey, int value)
    {
        if (bar == null) return;
        var range = AttrRangeConfig.Get(rangeKey);
        int filled = Mathf.Clamp(Mathf.CeilToInt(range.Ratio(value) * AttrBarSegments), 0, AttrBarSegments);

        bar.itemRenderer = (i, obj) =>
        {
            if (obj is UI_AttrValueItem cell && cell.m_filled != null)
            {
                if (i < filled) cell.m_filled.selectedIndex = 1;
                else cell.m_filled.selectedIndex = 0;
            }
        };
        bar.numItems = AttrBarSegments;
    }

    private void RefreshCharacterInfo()
    {
        int saveIndex = selectedSaveIndex;
        entity info = GetCharacterInfo(saveIndex);
        panel.m_label_character_name.text = DisplayName(info, saveIndex);
    }

    // 角色配表信息：InfoManager 由场景提供，缺失时返回默认值
    private static entity GetCharacterInfo(int saveIndex)
    {
        if (Tool.InfoManager == null) return default;
        return Tool.InfoManager.GetPlayerCharacterInfo(saveIndex);
    }

    // 列表条目与右侧标题共用一个名字口径：配表没名字时退回「进攻/防守角色 + 阵营内下标」
    private static string DisplayName(entity info, int saveIndex)
    {
        if (!string.IsNullOrEmpty(info.name)) return info.name;
        if (IsDefense(saveIndex))
            return MessageVariable.F(message_variablesEnum.home_roleName_defense, saveIndex - GlobalConfig.attack_character_count);
        return MessageVariable.F(message_variablesEnum.home_roleName_attack, saveIndex);
    }

    // 属性一律取整显示
    private static int[] ToValues(EntityAttribute attr)
    {
        return new[]
        {
            Mathf.RoundToInt(attr.health), Mathf.RoundToInt(attr.attack), Mathf.RoundToInt(attr.critRate),
            Mathf.RoundToInt(attr.critDamage), Mathf.RoundToInt(attr.viewDistance), Mathf.RoundToInt(attr.weaponSlotCount),
        };
    }

    // 浏览角色变化后重建场景里的预览模型
    private void RefreshPreview()
    {
        if (Tool.ClientLogicManager == null || Tool.ClientLogicManager.HomePreview == null) return;
        int saveIndex = selectedSaveIndex;
        EntityType type;
        if (IsDefense(saveIndex)) type = EntityType.Defense(saveIndex - GlobalConfig.attack_character_count);
        else type = EntityType.Attack(saveIndex);
        Tool.ClientLogicManager.HomePreview.RefreshHome(type);
    }

    private async void OnConnectClicked()
    {
        // 连接前校验玩家名字长度，不合法则飘字提示并拦截连接
        if (!TrySetPlayerName(ClientSelection.playerName, out int min, out int max))
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.name_length_invalid, min, max);
            return;
        }
        await ConnectAsync(panel.m_connectPanel.m_input_ipaddress.m_content.text);
    }

    // 连接唯一的发起入口：连接按钮与 AI 的连接命令共用（名字校验由调用方先走 TrySetPlayerName）
    private async Task<NetworkManager.ConnectResult> ConnectAsync(string ip)
    {
        if (Tool.NetworkManager == null)
            throw new System.InvalidOperationException("[首页] 场景缺少 NetworkManager，无法连接");
        if (Tool.SaveManager == null)
            throw new System.InvalidOperationException("[首页] 场景缺少 SaveManager，无法记录服务器地址");

        string serverIp = ip;
        if (serverIp == null) serverIp = "";
        Tool.SaveManager.lastServerIp = serverIp; // 空串落盘时写默认本机地址，退出时统一写
        var result = await Tool.NetworkManager.TryConnect(ip);
        if (result != NetworkManager.ConnectResult.Success) return result;
        Tool.NetworkManager.EnterWorld();
        return result;
    }
    #endregion
}
