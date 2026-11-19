using Ros.Config;
using Ros.Transport;
using Ros.UI.Main;
using UnityEngine;

// 上帝视角：窗口期内连按快捷键打开，自己订阅地图数据包并按帧刷新
public class GodViewView : PageBase
{
    private readonly UI_GodViewMinimapPanel panel;
    private int pressCount;
    private float firstPressTime;

    public GodViewView(UI_GodViewMinimapPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.god_view;

    public bool IsOpen => IsVisible;

    protected override void Init()
    {
        panel.Init();
        panel.Close();
        panel.m_panel.m_close.onClick.Set(Close);
        EventManager.AddEvent<SCGodViewEntities>(ClientEvent.OnGodViewEntities, OnEntities);
        EventManager.AddEvent<SCGodViewCorals>(ClientEvent.OnGodViewCorals, OnCorals);
        EventManager.AddEvent<SCGodViewEntityDetail>(ClientEvent.OnGodViewDetail, OnDetail);
    }

    public override void Shutdown()
    {
        EventManager.RemoveEvent<SCGodViewEntities>(ClientEvent.OnGodViewEntities, OnEntities);
        EventManager.RemoveEvent<SCGodViewCorals>(ClientEvent.OnGodViewCorals, OnCorals);
        EventManager.RemoveEvent<SCGodViewEntityDetail>(ClientEvent.OnGodViewDetail, OnDetail);
    }

    public override void Update(float deltaTime)
    {
        var battle = Battle;
        if (battle == null) return;
        panel.Tick(deltaTime, battle.MyCamp, BattlePage.OwnEntityId);
    }

    // 与 GM 面板同一套连按判定；已打开时不再累计，避免关闭后残留一半计数
    public void TickOpenKey()
    {
        if (panel.IsOpen)
        {
            ResetPress();
            return;
        }
        if (!Input.GetKeyDown(GlobalConfig.god_view_open_key)) return;
        if (pressCount == 0 || Time.unscaledTime - firstPressTime > GlobalConfig.god_view_open_press_window)
        {
            pressCount = 1;
            firstPressTime = Time.unscaledTime;
        }
        else pressCount++;
        if (pressCount < GlobalConfig.god_view_open_press_count) return;
        ResetPress();
        Open();
    }

    private void Open()
    {
        panel.Open(ResolveCenter());
        if (Tool.UIManager != null) Tool.UIManager.ShowPage(PageKey);
    }

    public void Close()
    {
        ResetPress();
        if (Tool.UIManager != null) Tool.UIManager.HidePage(PageKey);
    }

    public override void Exit()
    {
        panel.Close();
        base.Exit();
    }

    private void ResetPress()
    {
        pressCount = 0;
        firstPressTime = 0f;
    }

    // 初始区域中心 = 雷达圆心（本地实体 / 观战目标 / 自由视角相机），取不到就从地图正中开始
    private static Vector2 ResolveCenter()
    {
        var battle = Battle;
        if (battle != null && battle.TryGetMinimapCenter(out var center)) return new Vector2(center.x, center.z);
        float half = GlobalVariable.RangeLength(global_variablesEnum.terrain_size) * 0.5f;
        return new Vector2(half, half);
    }

    private void OnEntities(SCGodViewEntities chunk) => panel.OnEntities(chunk);

    private void OnCorals(SCGodViewCorals chunk) => panel.OnCorals(chunk);

    private void OnDetail(SCGodViewEntityDetail info) => panel.OnDetail(info);

    private static BattlePage Battle => Tool.UIManager != null ? Tool.UIManager.Battle : null;
}
