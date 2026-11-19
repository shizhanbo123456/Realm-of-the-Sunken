using FairyGUI;
using Ros.UI.Main;
using Ros.Config;
using UnityEngine;

// 电视机助理：常驻表现层，表情与台词全部来自 assistant_expression.csv
public class RobotTvPage : PageBase
{
    private const float PanelScale = 0.5f;
    private const float MarginX = 70f; // 距屏幕右边缘
    private const float MarginY = 15f; // 距屏幕下边缘

    private readonly UI_RobotTV panel;

    public RobotTvPage(UI_RobotTV panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.robot_tv;

    protected override void Init()
    {
        panel.Construct();
    }

    public override void Update(float deltaTime)
    {
        panel.Tick(deltaTime);
    }

    public override void Shutdown()
    {
        panel.Shutdown();
    }

    // 固定贴屏幕右下角，按缩放后的可见边缘留边距
    public override void OnResize(float width, float height)
    {
        panel.scale = new Vector2(PanelScale, PanelScale);
        panel.SetXY(
            GRoot.inst.width - panel.width * PanelScale - MarginX,
            GRoot.inst.height - panel.height * PanelScale - MarginY);
    }
}
