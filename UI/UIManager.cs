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
    private GuideView guide;

    // GM 面板打开期间接管键盘：角色操控、技能、指令面板、技能选择都不得响应
    public bool GmOpen => gm != null && gm.IsOpen;

    // 玩法输入归 UI：GM 面板打开、正在文字输入框里打字、设置或图鉴打开
    public bool InputCaptured => GmOpen || IsTypingInTextField || (settings != null && settings.IsOpen) || (guide != null && guide.IsOpen);

    private static bool IsTypingInTextField => GRoot.inst != null && GRoot.inst.focus is GTextInput;

    // 结算界面：独立子界面（不挂页面），战斗结束与 GM 演示都用它
    public SettlementView Settlement { get; private set; }

    // 聊天面板：AI 指令发消息也要走它的发送链路，外部从这里取
    public ChatPlugin Chat => chat;

    // 确认弹窗：独立子界面（不挂页面），弹出时自带全屏遮罩挡底层点击
    public ConfirmView Confirm { get; private set; }

    // 加载界面：独立子界面（不挂页面），默认隐藏，由外部 ShowLoading / HideLoading 控制
    private UI_LoadingPanel loading;
    private const int LoadingPageCount = 3; // m_page 档位数（0~2）

    // 电视机助理：常驻表现层，表情与台词全部来自 assistant_expression.csv
    private UI_RobotTV robotTv;
    private const float RobotTvScale = 0.5f;
    private const float RobotTvMarginX = 70f;  // 距屏幕右边缘
    private const float RobotTvMarginY = 15f;  // 距屏幕下边缘

    private PageBase currentPage;
    private int lastScreenWidth, lastScreenHeight;

    // 全局飘字（任意界面可用，屏幕上方居中、随时间上浮、透明度淡入淡出），用 UI_NoticePanel 承载
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
            Settlement = new SettlementView();
            Confirm = new ConfirmView();
            settings = new SettingsView();
            guide = new GuideView();

            loading = UI_LoadingPanel.CreateInstance();
            loading.m_show_bar.selectedIndex = 0; // 未设置进度前不显示进度条
            loading.visible = false;
            GRoot.inst.AddChild(loading);

            Debug.Log($"[UIManager] 页面已创建：home={home != null}, lobby={lobby != null}, battle={battle != null}, chat={chat != null}");

            //注册回调/渲染器（Construct 里会设置 GList.itemRenderer，必须在设置 numItems 之前完成）
            home.Construct();
            lobby.Construct();
            battle.Construct();
            chat.Construct();
            gm.Construct();
            Settlement.Construct();
            Confirm.Construct();
            settings.Construct();
            guide.Construct();
            settings.OnOpenGuide = OpenGuide;
            settings.OnOpenBridge = OpenBridgeConfirm;
            settings.OnExit = ExitFromSettings;
            Debug.Log("[UIManager] Construct 完成");

            ApplyResize();
            Debug.Log($"[UIManager] 适配完成：屏幕 {Screen.width}x{Screen.height}，面板宽 {home.PanelWidth:0}，缩放 {home.UiScale:0.00}，GRoot {GRoot.inst.width:0}x{GRoot.inst.height:0}");

            BeginStartupLoad();

            //切页事件（发送方在 NetworkManager）：连接成功→大厅、开战→战斗、断开/超时→首页
            EventManager.AddEvent(ClientEvent.OnConnect, OnConnect);
            EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
            EventManager.AddEvent(ClientEvent.OnRestartGame, OnRestartGame);
            EventManager.AddEvent(ClientEvent.OnExitWorld, OnExitWorld);
            EventManager.AddEvent<int>(ClientEvent.OnShowPrompt, OnShowPrompt);

            // 助理放最后创建：加在 GRoot 最上层，且它的配表校验失败不会影响上面整段初始化
            robotTv = UI_RobotTV.CreateInstance();
            GRoot.inst.AddChild(robotTv);
            robotTv.Construct();
            ApplyRobotTvLayout();
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
        if (chat != null) chat.Shutdown();
        if (gm != null) gm.Shutdown();
        if (Confirm != null) Confirm.Shutdown();
        if (Settlement != null) Settlement.Shutdown();
        if (robotTv != null) robotTv.Shutdown();
    }

    private void Update()
    {
        if (Screen.width != lastScreenWidth || Screen.height != lastScreenHeight) ApplyResize();
        if (currentPage != null) currentPage.Tick(Time.deltaTime);
        if (gm != null) gm.Tick();
        if (settings != null) settings.Tick();
        if (robotTv != null) robotTv.Tick(Time.deltaTime);
        TickFloating();
    }

    private void ApplyResize()
    {
        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        float width = 1080f * ((float)Screen.width / Screen.height);
        if (home != null) home.OnResize(width, 1080f);
        if (lobby != null) lobby.OnResize(width, 1080f);
        if (battle != null) battle.OnResize(width, 1080f);
        if (chat != null) chat.OnResize(Screen.height / 1080f);
        if (gm != null) gm.OnResize(Screen.height / 1080f);
        if (Confirm != null) Confirm.OnResize(Screen.height / 1080f);
        if (settings != null) settings.OnResize(Screen.height / 1080f);
        if (guide != null) guide.OnResize(Screen.height / 1080f);
        if (loading != null)
        {
            float scale = Screen.height / 1080f;
            loading.scale = new Vector2(scale, scale);
            loading.SetXY((GRoot.inst.width - loading.width * scale) * 0.5f,
                (GRoot.inst.height - loading.height * scale) * 0.5f);
        }
        if (robotTv != null) ApplyRobotTvLayout();
    }

    // 助理固定贴屏幕右下角，按缩放后的可见边缘留边距
    private void ApplyRobotTvLayout()
    {
        robotTv.scale = new Vector2(RobotTvScale, RobotTvScale);
        robotTv.SetXY(GRoot.inst.width - robotTv.width * RobotTvScale - RobotTvMarginX,
            GRoot.inst.height - robotTv.height * RobotTvScale - RobotTvMarginY);
    }

    #region 加载界面
    public bool LoadingVisible => loading != null && loading.visible;

    // 显示加载界面：每次随机挑一种页面档位，进度条先收起（等 SetLoadingProgress 再显示）
    public void ShowLoading()
    {
        if (loading == null) return;
        loading.m_page.selectedIndex = UnityEngine.Random.Range(0, LoadingPageCount);
        loading.m_show_bar.selectedIndex = 0;
        loading.visible = true;
    }

    public void HideLoading()
    {
        if (loading != null) loading.visible = false;
    }

    // 设置进度条信息：progress01 = 0~1 完成度，text 显示在进度文本上；设置即展开进度条
    public void SetLoadingProgress(float progress01, string text)
    {
        if (loading == null) return;
        loading.m_show_bar.selectedIndex = 1;
        loading.m_progressbar.value = Mathf.Clamp01(progress01) * loading.m_progressbar.max;
        loading.m_label.text = text;
    }

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
        if (currentPage != null) return; // 加载期间已经被切到别的页
        currentPage = home;
        home.Enter(null);
        Debug.Log($"[UIManager] 首页已显示：home.visible={home.IsVisible}，children={GRoot.inst.numChildren}");
    }
    #endregion

    // 飘字只按 prompt 表主键取文案；文案带 {0} 的用 args 填充
    public void ShowFlyText(promptEnum promptId, params object[] args)
    {
        string text = PromptConfig.Get(promptId).text;
        if (args != null && args.Length > 0) text = string.Format(text, args);

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
    public void ShowConfirm(string content, Action onConfirm, Action onCancel = null)
    {
        if (Confirm != null) Confirm.Show(content, onConfirm, onCancel);
    }

    // 关闭确认弹窗（不触发任何回调）
    public void CloseConfirm()
    {
        if (Confirm != null) Confirm.Hide();
    }

    public void TurnPage(PageType type, ShowParam param = null)
    {
        if (Confirm != null) Confirm.Hide(); // 切页时收起未决的确认弹窗，避免残留遮罩挡住新页面
        if (settings != null) settings.Hide(); // 设置/图鉴挂 GRoot，切页必须一起收起
        if (guide != null) guide.Hide();
        if (currentPage != null) currentPage.Exit();
        currentPage = GetPage(type);
        if (currentPage != null) currentPage.Enter(param);
    }

    // 当前根页面：三个页面共用一个设置面板，面板里的跳转要按它判断去哪
    public PageType CurrentPageType
    {
        get
        {
            if (currentPage == lobby) return PageType.Lobby;
            if (currentPage == battle) return PageType.Battle;
            return PageType.Home;
        }
    }

    // 打开设置：三个页面上的设置按钮都走这里
    public void OpenSettings()
    {
        if (guide != null) guide.Hide();
        if (settings != null) settings.Show();
    }

    // 设置里的「图鉴」：先关设置再开图鉴；图鉴关闭只收起自己，回到当前根页面
    private void OpenGuide()
    {
        if (settings != null) settings.Hide();
        if (guide != null) guide.Show();
    }

    // 设置里的「桥接」：只有 Home 能启动；已在跑就只提示；否则关设置 → 二次确认
    private void OpenBridgeConfirm()
    {
        if (CurrentPageType != PageType.Home)
        {
            ShowFlyText(promptEnum.bridge_unavailable);
            return;
        }
        if (RuntimeBridgeServer.IsRunning)
        {
            ShowFlyText(promptEnum.bridge_running);
            return;
        }

        if (settings != null) settings.Hide();
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
        PageType page = CurrentPageType;
        if (page == PageType.Home) return;

        if (settings != null) settings.Hide();
        string content = MessageVariable.V(page == PageType.Battle
            ? message_variablesEnum.ui_confirmQuitBattle
            : message_variablesEnum.ui_confirmLeaveRoom);
        ShowConfirm(content, DoExit);
    }

    private static void DoExit()
    {
        if (Tool.NetworkManager != null) Tool.NetworkManager.ExitWorld();
    }

    //结算在大厅之上播放期间由结算流程控制大厅面板显隐
    public void SetLobbyPanelVisible(bool visible)
    {
        if (lobby != null) lobby.SetPanelVisible(visible);
    }

    private PageBase GetPage(PageType type)
    {
        return type switch
        {
            PageType.Home =>home,
            PageType.Lobby => lobby,
            PageType.Battle => battle,
            _ => home,
        };
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

    private void OnConnect()
    {
        TurnPage(PageType.Lobby);
    }

    private void OnBattleStart(int camp)
    {
        TurnPage(PageType.Battle);
    }

    private void OnRestartGame()
    {
        ReturnToHome();
    }

    private void OnExitWorld()
    {
        ReturnToHome();
    }

    // 服务器提示是通用功能：与当前页面无关，收到即显示
    private void OnShowPrompt(int messageId)
    {
        ShowFlyText((promptEnum)messageId);
    }

    private void ReturnToHome()
    {
        if (Tool.ClientLogicManager != null) Tool.ClientLogicManager.EntityPlayers.ClearAll();
        TurnPage(PageType.Home);
    }
}

public enum PageType
{
    Home,
    Lobby,
    Battle
}
