using FairyGUI;
using Ros.Config;
using Ros.UI.Main;
using UnityEngine;

// 所有界面的统一基类：生命周期固定为 PageKey/配表 → Init → Enter/Update/Exit → OnResize
// 层级与遮罩开关全部读 page 表，代码里不再写界面配置
public abstract class PageBase
{
    protected GComponent Root { get; private set; }

    // 每个界面创建时复制一份全屏遮罩，挂在 Root 的最底层
    public UI_PanelBackground Mask { get; private set; }

    public page Config { get; private set; }

    public int Order => Config.order;

    private bool showBlackMask;
    private bool clickOutsideToClose;
    private System.Action outsideClose;

    // 运行期开关：赋值后立刻作用到遮罩上
    public bool ShowBlackMask
    {
        get => showBlackMask;
        set { showBlackMask = value; ApplyMask(); }
    }

    public bool ClickOutsideToClose
    {
        get => clickOutsideToClose;
        set { clickOutsideToClose = value; ApplyMask(); }
    }

    public bool IsVisible { get; private set; }

    public float UiScale { get; protected set; } = 1f;

    public float PanelWidth { get; private set; } = 1920f;

    private float layoutHeight = 1080f; // 设计高度基准，全屏适配时高度不变

    protected PageBase(GComponent root)
    {
        Root = root;
    }

    // 由 UIManager 调用一次：先按 page 表取配置，再做一次性初始化
    public void InitPage()
    {
        Config = PageConfig.Get(PageKey);
        if (Config.order == 0) // 层级没填就没法排序，直接暴露出来
            throw new System.InvalidOperationException($"[UI] {GetType().Name} 在 page 表里的层级未填");
        BuildMask();
        Init();
    }

    // 由 UIManager 注入：遮罩点击即收起本界面（同 HidePage）
    public void BindOutsideClose(System.Action close) => outsideClose = close;

    // 本界面在 page 表里的主键：Init 之前读，只读一次
    public abstract pageEnum PageKey { get; }

    // 一次性初始化：绑事件、建渲染器（原 Construct）
    protected virtual void Init() { }

    // 每次显示
    public virtual void Enter(ShowParam param)
    {
        IsVisible = true;
        Root.visible = true;
        ApplyLayout();
    }

    // 每帧，只对可见页面调用
    public virtual void Update(float deltaTime) { }

    // 每次隐藏
    public virtual void Exit()
    {
        IsVisible = false;
        Root.visible = false;
    }

    // 销毁前的清理：解事件订阅等
    public virtual void Shutdown() { }

    public virtual void OnResize(float width, float height)
    {
        layoutHeight = height;
        UiScale = Screen.height / height;
        PanelWidth = width;
        ApplyLayout();
    }

    // 适配：alignMode 0 = 全屏（高度不变、按窗口宽高比重设宽度后缩放到刚好铺满）；1~9 = 保持设计尺寸贴到屏幕九宫格
    private void ApplyLayout()
    {
        Root.pivotAsAnchor = false;
        Root.pivot = Vector2.zero;
        Root.scale = new Vector2(UiScale, UiScale);
        if (Config.alignMode == 0)
        {
            Root.SetSize(layoutHeight * Screen.width / Screen.height, layoutHeight);
            Root.SetXY(0f, 0f);
        }
        else
        {
            Root.SetXY(AnchorX(), AnchorY());
        }
        SyncMask();
    }

    // 1 左下 / 2 正下 / 3 右下 / 4 左中 / 5 正中 / 6 右中 / 7 左上 / 8 正上 / 9 右上
    private float AnchorX()
    {
        int col = (Config.alignMode - 1) % 3;
        float width = Root.width * UiScale;
        if (col == 0) return Config.marginX;
        if (col == 1) return (GRoot.inst.width - width) * 0.5f;
        return GRoot.inst.width - width - Config.marginX;
    }

    private float AnchorY()
    {
        int row = (Config.alignMode - 1) / 3; // 0 底 / 1 中 / 2 顶
        float height = Root.height * UiScale;
        if (row == 0) return GRoot.inst.height - height - Config.marginY;
        if (row == 1) return (GRoot.inst.height - height) * 0.5f;
        return Config.marginY;
    }

    // 复制一份全屏遮罩，作为面板最底层的子元素
    private void BuildMask()
    {
        Mask = UI_PanelBackground.CreateInstance();
        Root.AddChildAt(Mask, 0);
        Mask.m_full_screen_close.onClick.Set(OnMaskClicked);
        showBlackMask = Config.blackMask;
        clickOutsideToClose = Config.closeOnOutsideClick;
        ApplyMask();
    }

    // 两个开关都不开时整块遮罩不显示，也不会拦截点击
    private void ApplyMask()
    {
        if (Mask == null) return;
        Mask.visible = showBlackMask || clickOutsideToClose;
        Mask.m_black_mask.visible = showBlackMask;
        Mask.m_full_screen_close.visible = clickOutsideToClose;
        SyncMask();
    }

    // 遮罩铺满整屏并对到屏幕左上角：界面居中或贴边都能盖住整屏
    private void SyncMask()
    {
        if (Mask == null) return;
        Mask.SetSize(Screen.width / UiScale, Screen.height / UiScale);
        Mask.SetXY(-Root.x / UiScale, -Root.y / UiScale);
    }

    private void OnMaskClicked()
    {
        if (outsideClose == null)
            throw new System.InvalidOperationException($"[UI] {GetType().Name} 未绑定遮罩关闭回调");
        outsideClose();
    }

    // 挂到 GRoot：先挂载再 Init，Init 里就能直接操作显示对象
    public void AttachTo(GComponent parent)
    {
        parent.AddChild(Root);
        Root.visible = false;
    }

    // UIManager 按 Order 排层时要拿到显示对象
    public GComponent RootComponent => Root;

    // 通用面板底框（UI_panel_2）自带关闭按钮：点它收起整个面板，各面板不必再单独放关闭按钮
    protected static void BindFrameClose(UI_panel_2 frame, GComponent target)
    {
        if (frame == null) return;
        frame.m_close.onClick.Set(() => target.visible = false);
    }

    public Vector2 WorldToPanel(Vector3 worldPos, Camera camera = null)
    {
        var cam = camera != null ? camera : Camera.main;
        if (cam == null) return Vector2.zero;
        var screen = cam.WorldToScreenPoint(worldPos);
        // Unity 屏幕 y 轴向上（原点左下），FGUI 局部 y 轴向下（原点左上），需翻转
        return new Vector2(screen.x / UiScale, (Screen.height - screen.y) / UiScale);
    }
}

public class ShowParam
{
}
