using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

// 显示层级：Order 越大越靠上；同 Order 的页面互斥，ShowPage 新页面时自动顶掉旧的
public struct UiConfig
{
    public int Order;
}

// 显示层级常量：数字越大越靠上；同值的页面互斥（显示新的会顶掉旧的）
public static class UILayer
{
    public const int Page = 10;      // 首页 / 大厅 / 战斗
    public const int Chat = 20;
    public const int RobotTv = 25;
    public const int Gm = 30;
    public const int Dialog = 40;    // 设置 / 图鉴
    public const int Confirm = 50;
    public const int Loading = 999;
}

// 页面标识：UIManager.ShowPage / HidePage 的入参
public enum PageId
{
    Home,
    Lobby,
    Battle,
    Chat,
    RobotTv,
    Gm,
    Settings,
    Guide,
    Confirm,
    Loading,
}

// 所有界面的统一基类：生命周期固定为 InitConfig → Init → Enter/Update/Exit → OnResize
public abstract class PageBase
{
    protected GComponent Root { get; private set; }

    public UiConfig Config { get; private set; }

    public int Order => Config.Order;

    public bool IsVisible { get; private set; }

    public float UiScale { get; protected set; } = 1f;

    public float PanelWidth { get; private set; } = 1920f;

    protected PageBase(GComponent root)
    {
        Root = root;
    }

    // 由 UIManager 调用一次：先取显示层级，再做一次性初始化
    public void InitPage()
    {
        Config = InitConfig();
        if (Config.Order == 0) // 层级没填就没法排序，直接暴露出来
            throw new System.InvalidOperationException($"[UI] {GetType().Name} 的 UiConfig.Order 未设置");
        Init();
    }

    // 显示层级：Init 之前调用，只跑一次
    protected abstract UiConfig InitConfig();

    // 一次性初始化：绑事件、建渲染器（原 Construct）
    protected virtual void Init() { }

    // 每次显示
    public virtual void Enter(ShowParam param)
    {
        IsVisible = true;
        Root.visible = true;
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
        PanelWidth = width;
        UiScale = Screen.height / 1080f;
        Root.SetSize(width, height);
        Root.scale = new Vector2(UiScale, UiScale);
        Root.SetXY(0f, 0f);
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
