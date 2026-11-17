using System.Collections.Generic;
using FairyGUI;
using Ros.Config;
using Ros.UI.Main;
using UnityEngine;

// 图鉴子界面：m_buffTab 切技能/Buff 两页；技能页带详情与评分
public class GuideView
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
    private float uiScale = 1f;

    public bool IsOpen => panel != null && panel.visible;

    public void Construct()
    {
        panel = UI_GuidePanel.CreateInstance();
        panel.opaque = true;
        GRoot.inst.AddChild(panel);
        panel.visible = false;

        panel.m_btn_close.onClick.Set(Hide);
        panel.m_menu_btn_list.itemRenderer = RenderMenu;
        panel.m_menu_btn_list.numItems = MenuTitles.Length;
        panel.m_item_list.itemRenderer = RenderItem;

        var rankList = panel.m_skill_detail.m_rankList;
        rankList.defaultItem = UI_GuideDetailSkillRank.URL;
        rankList.itemRenderer = RenderRank;
        rankList.numItems = ScoreCount;
    }

    public void Show()
    {
        if (panel == null) return;
        GRoot.inst.AddChild(panel); // 每次弹出都重新置顶
        panel.visible = true;
        ApplyTransform();
        SwitchTab(tab);
    }

    public void Hide()
    {
        if (panel != null) panel.visible = false;
    }

    public void OnResize(float scale)
    {
        uiScale = scale;
        ApplyTransform();
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
        list.defaultItem = index == 0 ? UI_GuideItemSkill.URL : UI_GuideItemBuff.URL;
        list.numItems = entries.Count;

        if (index == 0 && entries.Count > 0) ShowDetail(0);
    }

    private void RenderItem(int index, GObject obj)
    {
        var entry = entries[index];
        var tex = entry.icon != null ? new NTexture(entry.icon) : null;
        if (tab == 0)
        {
            var item = (UI_GuideItemSkill)obj;
            item.title = entry.title;
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
            item.m_des.text = entry.description;
            item.m_slot.texture = tex;
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

    private void ShowDetail(int index)
    {
        var entry = entries[index];
        var detail = panel.m_skill_detail;
        detail.m_Name.text = entry.title;
        detail.m_slot.m_mask.m_icon.texture = entry.icon != null ? new NTexture(entry.icon) : null;
        detail.m_rank.m_level.selectedIndex = entry.quality;

        int[] scores = entry.scores;
        for (int i = 0; i < detail.m_rankList.numItems; i++)
        {
            var rank = (UI_GuideDetailSkillRank)detail.m_rankList.GetChildAt(i);
            int score = scores != null && i < scores.Length ? scores[i] : 0;
            rank.m_rank.m_level.selectedIndex = score;
            for (int j = 0; j < rank.m_rank_tabs.numItems; j++)
            {
                var scoreTab = (UI_GuideScoreTab)rank.m_rank_tabs.GetChildAt(j);
                scoreTab.m_highlight.selectedIndex = j < score + 1 ? 1 : 0;
            }
        }
    }

    private void ApplyTransform()
    {
        if (panel == null) return;
        panel.scale = new Vector2(uiScale, uiScale);
        panel.pivotAsAnchor = true;
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.SetXY(GRoot.inst.width * 0.5f, GRoot.inst.height * 0.5f);
    }
    #endregion
}
