using Ros.Config;
using Ros.UI.Main;

// 大地图：点小地图上的按钮打开，每帧按雷达收到的点位重铺整张地形图
public class LargeMinimapView : PageBase
{
    private readonly UI_LargeMinimapPanel panel;

    public LargeMinimapView(UI_LargeMinimapPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.large_minimap;

    public bool IsOpen => IsVisible;

    protected override void Init()
    {
        panel.Close();
        panel.m_panel.m_close.onClick.Set(Close);
    }

    // 只对可见页面调用：点位、阵营、朝向都从战斗会话取
    public override void Update(float deltaTime)
    {
        var battle = Battle;
        if (battle == null) return;
        bool hasYaw = battle.TryGetSelfYaw(out float yaw);
        panel.Refresh(battle.Minimap != null ? battle.Minimap.CurrentPoints : null, battle.MyCamp, yaw, hasYaw);
        panel.RefreshQuickMessages(battle.QuickMarks);
    }

    public void Open()
    {
        if (panel.IsOpen) return;
        panel.Open();
        if (Tool.UIManager != null) Tool.UIManager.ShowPage(PageKey);
    }

    public void Close()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(PageKey);
    }

    public override void Exit()
    {
        panel.Close();
        base.Exit();
    }

    public void ClearQuickMessages() => panel.ClearQuickMessages();

    private static BattlePage Battle => Tool.UIManager != null ? Tool.UIManager.Battle : null;
}
