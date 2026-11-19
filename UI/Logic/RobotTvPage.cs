using FairyGUI;
using Ros.UI.Main;
using Ros.Config;
using UnityEngine;

// 电视机助理：常驻表现层，表情与台词全部来自 assistant_expression.csv
public class RobotTvPage : PageBase
{
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
}
