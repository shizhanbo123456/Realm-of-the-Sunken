using System;
using System.Collections.Generic;
using FairyGUI;
using Ros.UI.Main;
using UnityRuntimeBridge;
using UnityEngine;

using Ros.Config;
public class UIManager : MonoBehaviour
{
    private HomePage home;
    private LobbyPage lobby;
    private BattlePage battle;
    private ChatPlugin chat;
    private GmPanel gm;
    private SettingsView settings;
    private RuleView rule;
    private GuideView guide;
    private ConfirmView confirm;
    private LoadingPage loading;
    private RobotTvPage robotTv;
    private SkillSelectView skillSelect;
    private LargeMinimapView largeMinimap;
    private GodViewView godView;
    private SelectCardView selectCard;

    // 注册表：ShowPage / HidePage 按 id 找页面，排层按 Order
    private readonly Dictionary<pageEnum, PageBase> pages = new();
    private readonly List<PageBase> pageList = new();
    private readonly List<PageBase> orderBuffer = new();

    // GM 面板打开期间接管键盘：角色操控、技能、指令面板、技能选择都不得响应
    public bool GmOpen => IsPageVisible(pageEnum.gm);

    // 玩法输入归 UI：GM 面板 / 设置 / 规则 / 图鉴 / 正在文字输入框里打字
    public bool InputCaptured => GmOpen || IsPageVisible(pageEnum.settings) || IsPageVisible(pageEnum.rule) || IsPageVisible(pageEnum.guide) || IsTypingInTextField;

    private static bool IsTypingInTextField => GRoot.inst != null && GRoot.inst.focus is GTextInput;

    // 结算界面：独立子界面，不参与页面排层（每次结算重建面板）
    public SettlementView Settlement { get; private set; }

    // 四个从战斗界面抽出去的独立界面：各自管开合与刷新，这里只给入口
    public BattlePage Battle => battle;

    // 聊天面板：GM 面板的输出也要往系统栏写一条
    public ChatPlugin Chat => chat;

    public SkillSelectView SkillSelect => skillSelect;

    public LargeMinimapView LargeMinimap => largeMinimap;

    public GodViewView GodView => godView;

    public SelectCardView SelectCard => selectCard;

    public bool LoadingVisible => IsPageVisible(pageEnum.loading);

    private int lastScreenWidth, lastScreenHeight;    // 全局飘字（任意界面可用，屏幕上方居中、随时间上浮、透明度淡入淡出），用 UI_NoticePanel 承载
    private readonly List<FloatingLabel> floatingLabels = new();
    private const float FloatingLife = 2.5f;   // 总存活时长
    private const float FloatRise = 90f;       // 存活期内上移的设计像素
    private const float FadeIn = 0.3f;         // 淡入时长
    private const float FadeOut = 0.6f;        // 淡出时长

    private class FloatingLabel
    {
        public GComponent comp;
        public float startTime;
        public float startY;
    }

    #region 页面生命周期
    private void Start()
    {
        try
        {
            Tool.UIManager = this;
            Debug.Log("[UIManager] 启动：加载 Main 包...");

            UIPackage.AddPackage("GUI/Main");
            Debug.Log($"[UIManager] 包已加载：{(UIPackage.GetByName("Main") != null ? "成功" : "失败")}");
            MainBinder.BindAll();

            home = new HomePage(UI_HomePanel.CreateInstance());
            lobby = new LobbyPage(UI_LobbyPanel.CreateInstance());
            battle = new BattlePage(UI_BattlePanel.CreateInstance());
            chat = new ChatPlugin(UI_ChatPanel.CreateInstance());
            gm = new GmPanel(UI_GmPanel.CreateInstance());
            settings = new SettingsView(UI_SettingsPanel.CreateInstance());
            rule = new RuleView(UI_RulePanel.CreateInstance());
            guide = new GuideView(UI_GuidePanel.CreateInstance());
            confirm = new ConfirmView(UI_ConfirmPanel.CreateInstance());
            loading = new LoadingPage(UI_LoadingPanel.CreateInstance());
            robotTv = new RobotTvPage(UI_RobotTV.CreateInstance());
            skillSelect = new SkillSelectView(UI_SkillSelectPanel.CreateInstance());
            largeMinimap = new LargeMinimapView(UI_LargeMinimapPanel.CreateInstance());
            godView = new GodViewView(UI_GodViewMinimapPanel.CreateInstance());
            selectCard = new SelectCardView(UI_SelectCardPanel.CreateInstance());
            Settlement = new SettlementView();

            Register(home);
            Register(lobby);
            Register(battle);
            Register(chat);
            Register(robotTv);
            Register(skillSelect);
            Register(largeMinimap);
            Register(godView);
            Register(selectCard);
            Register(gm);
            Register(settings);
            Register(rule);
            Register(guide);
            Register(confirm);
            Register(loading);

            // 先全部挂到 GRoot，再统一 Init：Init 里操作显示对象时父节点已就位；层级最后按 Order 排
            for (int i = 0; i < pageList.Count; i++) pageList[i].AttachTo(GRoot.inst);
            for (int i = 0; i < pageList.Count; i++) pageList[i].InitPage();
            Debug.Log($"[UIManager] 页面已初始化：{pageList.Count} 个");

            settings.OnOpenRule = () => ShowPage(pageEnum.rule);
            settings.OnOpenGuide = () => ShowPage(pageEnum.guide);
            settings.OnOpenBridge = OpenBridgeConfirm;
            settings.OnExit = ExitFromSettings;
            Settlement.Construct();

            // 助理是常驻表现层，开机就显示
            ShowPage(pageEnum.robot_tv);

            ApplyResize();
            Debug.Log($"[UIManager] 适配完成：屏幕 {Screen.width}x{Screen.height}，面板宽 {home.PanelWidth:0}，缩放 {home.UiScale:0.00}，GRoot {GRoot.inst.width:0}x{GRoot.inst.height:0}");

            BeginStartupLoad();

            //切页事件（发送方在 NetworkManager）：连接成功→大厅、开战→战斗、断开/超时→首页
            EventManager.AddEvent(ClientEvent.OnConnect, OnConnect);
            EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
            EventManager.AddEvent(ClientEvent.OnRestartGame, OnRestartGame);
            EventManager.AddEvent(ClientEvent.OnExitWorld, OnExitWorld);
            EventManager.AddEvent<int>(ClientEvent.OnShowPrompt, OnShowPrompt);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e); // 任何被吞掉的异常都显式暴露
        }
    }

    private void OnDestroy()
    {
        //EventManager 是静态的，订阅必须与解绑成对，否则跨场景累积
        EventManager.RemoveEvent(ClientEvent.OnConnect, OnConnect);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, OnRestartGame);
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, OnExitWorld);
        EventManager.RemoveEvent<int>(ClientEvent.OnShowPrompt, OnShowPrompt);
        for (int i = 0; i < pageList.Count; i++) pageList[i].Shutdown();
        if (Settlement != null) Settlement.Shutdown();
    }

    private void Update()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight) ApplyResize();

        float deltaTime = Time.deltaTime;
        for (int i = 0; i < pageList.Count; i++)
        {
            PageBase page = pageList[i];
            if (page.IsVisible) page.Update(deltaTime);
        }
        TickFloating();
    }

    private void ApplyResize()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        float width = 1080f * ((float)Screen.width / Screen.height);
        for (int i = 0; i < pageList.Count; i++) pageList[i].OnResize(width, 1080f);
    }

    // 主键由界面自己声明（PageKey），这里只做去重与登记
    private void Register(PageBase page)
    {
        pageEnum id = page.PageKey;
        if (pages.ContainsKey(id))
            throw new System.InvalidOperationException($"[UI] 页面 {id} 重复注册");
        pages.Add(id, page);
        pageList.Add(page);
        page.BindOutsideClose(() => HidePage(id)); // 遮罩点击统一走 HidePage
    }
    #endregion

    #region 显示与层级
    // 唯一的显示入口：同 Order 的页面互斥，先顶掉旧的再显示新的
    public void ShowPage(pageEnum id, ShowParam param = null)
    {
        PageBase page = Require(id);
        for (int i = 0; i < pageList.Count; i++)
        {
            PageBase other = pageList[i];
            if (other == page || other.Order != page.Order || !other.IsVisible) continue;
            other.Exit();
        }
        if (!page.IsVisible) page.Enter(param);
        ApplyPageOrder();
    }

    // 唯一的隐藏入口：状态保留，只是不再显示、不再 Update
    public void HidePage(pageEnum id)
    {
        PageBase page = Require(id);
        if (page.IsVisible) page.Exit();
        ApplyPageOrder();
    }

    public bool IsPageVisible(pageEnum id) => pages.TryGetValue(id, out var page) && page.IsVisible;

    private PageBase Require(pageEnum id)
    {
        if (!pages.TryGetValue(id, out var page))
            throw new System.InvalidOperationException($"[UI] 页面 {id} 没有注册");
        return page;
    }

    // 按 Order 把页面排成连续的块：块内按 Order 升序，飞字之类的临时节点留在块之上
    private void ApplyPageOrder()
    {
        orderBuffer.Clear();
        orderBuffer.AddRange(pageList);
        orderBuffer.Sort((a, b) => a.Order.CompareTo(b.Order));

        int baseIndex = int.MaxValue;
        for (int i = 0; i < orderBuffer.Count; i++)
        {
            GComponent root = orderBuffer[i].RootComponent;
            if (root.parent != GRoot.inst) continue;
            baseIndex = Mathf.Min(baseIndex, GRoot.inst.GetChildIndex(root));
        }
        if (baseIndex == int.MaxValue) return;

        for (int i = 0; i < orderBuffer.Count; i++)
        {
            GComponent root = orderBuffer[i].RootComponent;
            if (root.parent != GRoot.inst) continue;
            GRoot.inst.SetChildIndex(root, Mathf.Min(baseIndex + i, GRoot.inst.numChildren - 1));
        }
    }

    // 切页时收起所有浮层，避免旧遮罩挡住新页面
    private void HideOverlays()
    {
        HidePage(pageEnum.confirm);
        HidePage(pageEnum.settings);
        HidePage(pageEnum.rule);
        HidePage(pageEnum.guide);
        // 战斗内的独立界面：切场景时一并收起，避免留在新界面上
        HidePage(pageEnum.skill_select);
        HidePage(pageEnum.large_minimap);
        HidePage(pageEnum.god_view);
        HidePage(pageEnum.select_card);
    }
    #endregion

    #region 加载界面
    // 显示加载界面：每次随机挑一种页面档位，进度条先收起（等 SetLoadingProgress 再显示）
    public void ShowLoading() => ShowPage(pageEnum.loading);

    public void HideLoading() => HidePage(pageEnum.loading);

    public void SetLoadingProgress(float progress01, string text) => loading.SetProgress(progress01, text);

    // 进入游戏：先挂加载界面把常驻资源拉齐（攻守角色/武器/两张布置图/地形素材表），完成后再进首页
    private void BeginStartupLoad()
    {
        ShowLoading();
        SetLoadingProgress(0f, MessageVariable.F(message_variablesEnum.ui_loadingResource, 0));

        if (Tool.ResourceManager == null)
        {
            Debug.LogError("[UIManager] 场景缺少 ResourceManager：跳过资源预加载直接进首页");
            EnterHome();
            return;
        }

        Tool.ResourceManager.LoadNecessary(EnterHome, OnNecessaryProgress);
    }

    private void OnNecessaryProgress(float progress01)
    {
        SetLoadingProgress(progress01, MessageVariable.F(message_variablesEnum.ui_loadingResource, Mathf.RoundToInt(progress01 * 100f)));
    }

    private void EnterHome()
    {
        HideLoading();
        ShowPage(pageEnum.home);
        Debug.Log($"[UIManager] 首页已显示：home.visible={home.IsVisible}，children={GRoot.inst.numChildren}");
    }
    #endregion

    // 飘字只按 prompt 表主键取文案；文案带 {0} 的用 args 填充
    public void ShowFlyText(promptEnum promptId, params object[] args)
    {
        string text = PromptConfig.Get(promptId).text;
        if (args != null && args.Length > 0) text = string.Format(text, args);

        AiObservation.ReportPrompt(text); // 玩家能看到的提示，AI 也记一份

        var panel = UI_NoticePanel.CreateInstance();
        if (panel.m_title != null) panel.m_title.text = text;
        GRoot.inst.AddChild(panel);
        float x = (GRoot.inst.width - panel.width) * 0.5f;
        float y = 140f;
        panel.SetXY(x, y);
        panel.alpha = 0f; // 从透明起步，由 TickFloating 淡入
        floatingLabels.Add(new FloatingLabel { comp = panel, startTime = Time.time, startY = y });
    }

    // 弹出确认弹窗：content = 正文，onConfirm = 确认回调，onCancel = 取消回调（可为空）
    public void ShowConfirm(string content, Action onConfirm, Action onCancel = null) => confirm.Show(content, onConfirm, onCancel);

    // 关闭确认弹窗（不触发任何回调）
    public void CloseConfirm() => HidePage(pageEnum.confirm);

    // 打开设置：三个页面上的设置按钮都走这里
    public void OpenSettings() => ShowPage(pageEnum.settings);

    // 设置里的「桥接」：只有 Home 能启动；已在跑就只提示；否则关设置 → 二次确认
    private void OpenBridgeConfirm()
    {
        if (!IsPageVisible(pageEnum.home))
        {
            ShowFlyText(promptEnum.bridge_unavailable);
            return;
        }
        if (RuntimeBridgeServer.IsRunning)
        {
            ShowFlyText(promptEnum.bridge_running);
            return;
        }

        HidePage(pageEnum.settings);
        ShowConfirm(MessageVariable.V(message_variablesEnum.ui_confirmStartBridge), StartBridge);
    }

    private void StartBridge()
    {
        RuntimeBridgeServer.Start();
        if (!RuntimeBridgeServer.IsRunning)
        {
            Debug.LogError("[桥接] 启动失败：" + RuntimeBridgeServer.LastError);
            ShowFlyText(promptEnum.bridge_start_failed, RuntimeBridgeServer.LastError);
            return;
        }
        ShowFlyText(promptEnum.bridge_running);
    }

    // 设置里的「退出」：首页没有可退出的东西；大厅退房间、对局退本局
    private void ExitFromSettings()
    {
        string content;
        if (IsPageVisible(pageEnum.battle)) content = MessageVariable.V(message_variablesEnum.ui_confirmQuitBattle);
        else if (IsPageVisible(pageEnum.lobby)) content = MessageVariable.V(message_variablesEnum.ui_confirmLeaveRoom);
        else return;

        HidePage(pageEnum.settings);
        ShowConfirm(content, DoExit);
    }

    private static void DoExit()
    {
        if (Tool.NetworkManager != null) Tool.NetworkManager.ExitWorld();
    }

    private void TickFloating()
    {
        for (int i = floatingLabels.Count - 1; i >= 0; i--)
        {
            var item = floatingLabels[i];
            float elapsed = Time.time - item.startTime;
            if (elapsed >= FloatingLife)
            {
                if (item.comp != null) item.comp.Dispose();
                floatingLabels.RemoveAt(i);
                continue;
            }
            // 上浮：存活期内匀速上移
            item.comp.y = item.startY - FloatRise * (elapsed / FloatingLife);
            // 透明度：开头淡入、结尾淡出，中间保持不透明
            float fadeIn = Mathf.Clamp01(elapsed / FadeIn);
            float fadeOut = Mathf.Clamp01((FloatingLife - elapsed) / FadeOut);
            item.comp.alpha = Mathf.Min(fadeIn, fadeOut);
        }
    }

    #region 事件
    private void OnConnect()
    {
        HideOverlays();
        ShowPage(pageEnum.lobby);
        ShowPage(pageEnum.chat); // 联网后聊天面板常驻，大厅与战斗共用
    }

    private void OnBattleStart(int camp)
    {
        HideOverlays();
        ShowPage(pageEnum.battle);
    }

    private void OnRestartGame() => ReturnToHome();

    private void OnExitWorld() => ReturnToHome();

    // 服务器提示是通用功能：与当前页面无关，收到即显示
    private void OnShowPrompt(int messageId) => ShowFlyText((promptEnum)messageId);

    private void ReturnToHome()
    {
        if (Tool.ClientLogicManager != null) Tool.ClientLogicManager.EntityPlayers.ClearAll();
        HideOverlays();
        HidePage(pageEnum.chat);
        ShowPage(pageEnum.home);
    }
    #endregion
}
