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
        if (tipKeys.Count > 0) SelectTip(0);
    }

    private static void CloseSelf()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.rule);
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
            tab.title = SettingTipsConfig.content[tipKeys[i]].name; // 条目子文本命名为 title，走 GButton 内置 title
            tab.selected = i == tipTab;
            int index = i;
            tab.onClick.Set(() => SelectTip(index));
        };

        tabs.numItems = tipKeys.Count;

        panel.m_contentList.numItems = 1; // 正文容器是列表，唯一元素只用于撑高滚动
    }

    private void SelectTip(int index)
    {
        tipTab = index;
        var tabs = panel.m_rule_buttons;
        for (int i = 0; i < tabs.numChildren; i++)
            tabs.GetChildAt(i).asButton.selected = i == index; // 页签选中态

        var item = panel.m_contentList.GetChildAt(0) as UI_RuleContent;
        item.m_cotent.text = tipTexts[index];
        panel.m_contentList.scrollPane.posY = 0; // 切页签回到顶部
    }
    #endregion
}
