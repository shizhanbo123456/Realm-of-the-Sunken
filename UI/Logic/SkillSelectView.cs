using System.Collections.Generic;
using Ros.Config;
using Ros.UI.Main;

// 技能选择：独立界面，勾选、上限拦截与提交都在这里；技能栏绘制仍归战斗界面
public class SkillSelectView : PageBase
{
    private readonly UI_SkillSelectPanel panel;

    public SkillSelectView(UI_SkillSelectPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.skill_select;

    public bool IsOpen => IsVisible;

    protected override void Init()
    {
        panel.Build(ApplySelection);
        EventManager.AddEvent(ClientEvent.OnSkillBarRefresh, OnSkillBarRefresh);
    }

    public override void Shutdown()
    {
        EventManager.RemoveEvent(ClientEvent.OnSkillBarRefresh, OnSkillBarRefresh);
    }

    // 快捷键：打开期间再按一次 = 关闭并应用（与底框关闭按钮同一路径）
    public void Toggle()
    {
        if (Tool.UIManager == null) return;
        if (panel.IsOpen)
        {
            panel.Commit();
            return;
        }
        var skills = Skills;
        var battle = Battle;
        if (skills == null || battle == null || !battle.CanOpenSkillSelect) return;

        panel.Open(skills.HeldSkills, skills.ActiveSkills, skills.SlotCapacity);
        Tool.UIManager.ShowPage(PageKey);
    }

    public override void Exit()
    {
        panel.Close();
        base.Exit();
    }

    private static ClientSkillManager Skills => Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;

    private static BattlePage Battle => Tool.UIManager != null ? Tool.UIManager.Battle : null;

    // 持有表变化：打开期间新获得的技能要进列表
    private void OnSkillBarRefresh()
    {
        if (panel.IsOpen && Skills != null) panel.RefreshHeld(Skills.HeldSkills);
    }

    // 关闭即应用：与当前技能栏一致就不上行（随手开一下就关不该清栏）
    private void ApplySelection()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(PageKey);
        var skills = Skills;
        if (skills == null || SameSkillSet(panel.Selection, skills.ActiveSkills)) return;
        skills.TrySetActiveSkills(panel.Selection);
        var battle = Battle;
        if (battle != null) battle.RefreshSkillBarNow();
    }

    // 栏内顺序由服务器按 id 升序排，勾选顺序无意义，按集合比
    private static bool SameSkillSet(IReadOnlyList<int> a, IReadOnlyList<int> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            bool found = false;
            for (int j = 0; j < b.Count; j++)
            {
                if (b[j] != a[i]) continue;
                found = true;
                break;
            }
            if (!found) return false;
        }
        return true;
    }
}
