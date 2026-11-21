using System.Collections.Generic;
using FairyGUI;
using Ros.Config;
using Ros.UI.Main;
using UnityEngine;

// 图鉴子界面：m_buffTab 切技能/Buff 两页；技能页带详情与评分
public class GuideView : PageBase
{
    private const int ScoreCount = 6;
    private const int ScoreTabCount = 5;

    private static readonly message_variablesEnum[] MenuTitles =
    {
        message_variablesEnum.guide_menu_skill, message_variablesEnum.guide_menu_buff,
    };

    private static readonly message_variablesEnum[] ScoreNames =
    {
        message_variablesEnum.skill_scoreName_output,
        message_variablesEnum.skill_scoreName_cooldown,
        message_variablesEnum.skill_scoreName_control,
        message_variablesEnum.skill_scoreName_support,
        message_variablesEnum.skill_scoreName_range,
        message_variablesEnum.skill_scoreName_attackSpeed,
    };

    private readonly List<GuideCatalog.Entry> entries = new();
    private UI_GuidePanel panel;
    private int tab;
    private int selectedIndex = -1;

    public bool IsOpen => IsVisible;

    public GuideView(UI_GuidePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.guide;

    protected override void Init()
    {
        panel.opaque = true;

        panel.m_btn_close.onClick.Set(CloseSelf);
        panel.m_menu_btn_list.itemRenderer = RenderMenu;
        panel.m_menu_btn_list.numItems = MenuTitles.Length;
        panel.m_item_list.itemRenderer = RenderItem;

        var rankList = panel.m_skill_detail.m_rankList;
        rankList.defaultItem = UI_GuideDetailSkillRank.URL;
        rankList.itemRenderer = RenderRank;
        rankList.numItems = ScoreCount;
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        SwitchTab(tab);
    }

    private static void CloseSelf()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.guide);
    }

    #region//Local
    private void RenderMenu(int index, GObject obj)
    {
        var btn = (GButton)obj;
        btn.title = MessageVariable.V(MenuTitles[index]);
        btn.selected = index == tab;
        int menuIndex = index;
        btn.onClick.Set(() => SwitchTab(menuIndex));
    }

    private void SwitchTab(int index)
    {
        tab = index;
        selectedIndex = -1;
        panel.m_buffTab.selectedIndex = index;
        var menu = panel.m_menu_btn_list;
        for (int i = 0; i < menu.numChildren; i++)
            menu.GetChildAt(i).asButton.selected = i == index;
        entries.Clear();
        if (index == 0)
        {
            entries.AddRange(GuideCatalog.BuildSkillEntries());
            panel.m_skill_detail.visible = true;
        }
        else
        {
            entries.AddRange(GuideCatalog.BuildBuffEntries());
            panel.m_skill_detail.visible = false;
        }

        var list = panel.m_item_list;
        list.numItems = 0;
        if (index == 0) list.defaultItem = UI_GuideItemSkill.URL;
        else list.defaultItem = UI_GuideItemBuff.URL;
        list.numItems = entries.Count;

        if (index == 0 && entries.Count > 0) ShowDetail(0);
    }

    private void RenderItem(int index, GObject obj)
    {
        var entry = entries[index];
        NTexture tex = null;
        if (entry.icon != null) tex = new NTexture(entry.icon);
        if (tab == 0)
        {
            var item = (UI_GuideItemSkill)obj;
            item.title = entry.title;
            item.selected = index == selectedIndex;
            item.m_des.text = entry.description;
            item.m_mask.m_mask.m_icon.texture = tex;
            item.m_Level.selectedIndex = entry.quality;
            item.m_rare_BtoS.m_level.selectedIndex = entry.quality;
            item.onClick.Set(() => ShowDetail(index));
        }
        else
        {
            var item = (UI_GuideItemBuff)obj;
            item.title = entry.title;
            item.selected = index == selectedIndex;
            item.m_des.text = entry.description;
            item.m_slot.texture = tex;
            int buffIndex = index;
            item.onClick.Set(() => SelectItem(buffIndex));
        }
    }

    private void RenderRank(int index, GObject obj)
    {
        var rank = (UI_GuideDetailSkillRank)obj;
        rank.m_rank_name.text = MessageVariable.V(ScoreNames[index]);
        if (rank.m_rank_tabs.numItems != ScoreTabCount)
        {
            rank.m_rank_tabs.defaultItem = UI_GuideScoreTab.URL;
            rank.m_rank_tabs.numItems = ScoreTabCount;
        }
    }

    private void SelectItem(int index)
    {
        selectedIndex = index;
        var itemList = panel.m_item_list;
        for (int i = 0; i < itemList.numChildren; i++)
            itemList.GetChildAt(i).asButton.selected = i == index;
    }

    private void ShowDetail(int index)
    {
        SelectItem(index);
        var entry = entries[index];
        var detail = panel.m_skill_detail;
        detail.m_Name.text = entry.title;
        if (entry.icon != null) detail.m_slot.m_mask.m_icon.texture = new NTexture(entry.icon);
        else detail.m_slot.m_mask.m_icon.texture = null;
        detail.m_rank.m_level.selectedIndex = entry.quality;

        int[] scores = entry.scores;
        for (int i = 0; i < detail.m_rankList.numItems; i++)
        {
            var rank = (UI_GuideDetailSkillRank)detail.m_rankList.GetChildAt(i);
            int score = 0;
            if (scores != null && i < scores.Length) score = scores[i];
            rank.m_rank.m_level.selectedIndex = score;
            for (int j = 0; j < rank.m_rank_tabs.numItems; j++)
            {
                var scoreTab = (UI_GuideScoreTab)rank.m_rank_tabs.GetChildAt(j);
                if (j < score + 1) scoreTab.m_highlight.selectedIndex = 1;
                else scoreTab.m_highlight.selectedIndex = 0;
            }
        }
    }
    #endregion
}
