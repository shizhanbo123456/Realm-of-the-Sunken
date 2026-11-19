using System.Collections.Generic;
using FairyGUI;
using Ros.Config;
using Ros.UI.Main;
using UnityEngine;

// 规则子界面：页签按 setting_tips 配表行生成，点页签把该行内容填进正文
public class RuleView : PageBase
{
    private readonly List<int> tipKeys = new();
    private readonly List<string> tipTexts = new();
    private int tipTab;

    private UI_RulePanel panel;

    public RuleView(UI_RulePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.rule;

    protected override void Init()
    {
        panel.opaque = true;
        panel.touchable = true;
        panel.m_panel.m_close.onClick.Set(CloseSelf);

        BuildTips();
    }

    // 每次打开都从第一个页签开始
    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        ApplyTransform();
        if (tipKeys.Count > 0) SelectTip(0);
    }

    private static void CloseSelf()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.rule);
    }

    public override void OnResize(float width, float height)
    {
        UiScale = Screen.height / 1080f;
        ApplyTransform();
    }

    #region//Local
    private void BuildTips()
    {
        var keys = new List<int>(SettingTipsConfig.content.Keys);
        keys.Sort();
        foreach (int key in keys)
        {
            tipKeys.Add(key);
            tipTexts.Add(SettingTipsConfig.content[key].text.Replace(@"{\n}", "\n"));
        }

        var tabs = panel.m_rule_buttons;
        tabs.itemRenderer = (i, obj) =>
        {
            var tab = obj.asButton;
            // 页签标题写在条目自带的文本上（FGUI 里这个文本没命名）
            var label = tab.GetChild("n3") as GTextField;
            if (label != null) label.text = SettingTipsConfig.content[tipKeys[i]].name;
            tab.selected = i == tipTab;
            int index = i;
            tab.onClick.Set(() => SelectTip(index));
        };

        tabs.numItems = tipKeys.Count;
    }

    private void SelectTip(int index)
    {
        tipTab = index;
        var tabs = panel.m_rule_buttons;
        for (int i = 0; i < tabs.numChildren; i++)
            tabs.GetChildAt(i).asButton.selected = i == index; // 页签选中态
        panel.m_rule_content.text = tipTexts[index];
    }

    // 与确认弹窗同规则：按中心锚点居中缩放
    private void ApplyTransform()
    {
        if (panel == null) return;
        panel.scale = new Vector2(UiScale, UiScale);
        panel.pivotAsAnchor = true;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.SetXY(GRoot.inst.width * 0.5f, GRoot.inst.height * 0.5f);
    }
    #endregion
}
