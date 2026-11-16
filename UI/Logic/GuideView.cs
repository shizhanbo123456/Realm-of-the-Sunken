using System.Collections.Generic;
using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

// 图鉴子界面：独立创建、挂 GRoot；关闭只收起自己，回到根界面（不回开设置）
public class GuideView
{
    private static readonly string[] MenuTitles = { "技能", "Buff" };

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

        panel.m_btn_back.onClick.Set(Hide);
        panel.m_menu_btn_list.itemRenderer = RenderMenu;
        panel.m_menu_btn_list.numItems = MenuTitles.Length;
        panel.m_item_list.itemRenderer = RenderItem;
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
        var item = (UI_GuideMenuLabel)obj;
        item.m_title.text = MenuTitles[index];
        item.touchable = true;
        item.onClick.Set(() => SwitchTab(index));
    }

    private void SwitchTab(int index)
    {
        tab = index;
        entries.Clear();
        entries.AddRange(index == 0 ? GuideCatalog.BuildSkillEntries() : GuideCatalog.BuildBuffEntries());
        panel.m_item_list.numItems = entries.Count;
    }

    private void RenderItem(int index, GObject obj)
    {
        var item = (UI_GuideItem)obj;
        var entry = entries[index];
        item.m_icon.texture = entry.icon != null ? new NTexture(entry.icon) : null;
        item.m_title.text = entry.title;
        item.m_des.text = entry.description;
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
