using System.Collections.Generic;
using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

// 首页：角色混排列表（攻 0~5 / 守 6~11）+ 属性面板 + 连接；改名改为点击玩家名进入
public class HomePage : PageBase
{
    private static readonly string[] StatKeys = { "生命", "攻击", "暴击", "暴伤", "视野", "技能槽" };
    private static readonly string[] AttrSuffixes = { "", "", "%", "%", "m", "" }; // 与 StatKeys 一一对应
    private const int AttrBarSegments = 10; // 属性条格数（UI_AttrItem.m_progressBar 里的 UI_AttrValueItem 数量）
    private static readonly string[] GuideMenuTitles = { "技能", "Buff" };

    private readonly UI_HomePanel panel;
    private UI_RemanePanel renamePanel; // 改名弹窗：设计自带全屏遮罩，挂 GRoot 置顶
    private UI_GuidePanel guidePanel;   // 图鉴：独立面板，挂 GRoot
    private readonly List<GuideCatalog.Entry> guideEntries = new();
    private int guideTab;

    public HomePage(UI_HomePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override void Construct()
    {
        var selection = panel.m_character_selection_panel;
        selection.m_characterList.itemRenderer = RenderCharacter;

        // 玩家改名：新版首页没有改名按钮，改为点击名字
        panel.m_playerInfo.m_label_playerName.touchable = true;
        panel.m_playerInfo.m_label_playerName.onClick.Add(OpenRename);
        BuildRenamePanel();

        panel.m_character_attr_panel.m_btn_select_character.onClick.Add(ConfirmSelection);

        panel.m_btn_open_guide.onClick.Add(OpenGuide);
        BuildGuidePanel();

        panel.m_connectPanel.m_btn_connect.title = "连接";
        panel.m_connectPanel.m_btn_connect.onClick.Add(OnConnectClicked);
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        RefreshPlayerName();
        Refresh();
        RefreshPreview();
        if (renamePanel != null) renamePanel.visible = false;
        if (guidePanel != null) guidePanel.visible = false;
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetHomeView();
    }

    public override void Exit()
    {
        base.Exit();
        if (renamePanel != null) renamePanel.visible = false; // 弹窗挂 GRoot，不收起来会留在别的页面上
        if (guidePanel != null) guidePanel.visible = false;
    }

    public override void OnResize(float width, float height)
    {
        base.OnResize(width, height);
        ApplyRenameTransform();
        ApplyGuideTransform();
    }

    #region//Local
    private void BuildRenamePanel()
    {
        renamePanel = UI_RemanePanel.CreateInstance();
        renamePanel.opaque = true;   // 吞掉点击：弹窗开着时首页与连接按钮都不响应
        renamePanel.touchable = true;
        GRoot.inst.AddChild(renamePanel);
        renamePanel.visible = false;

        renamePanel.m_input.maxLength = ConfigManager.GlobalVariableConfig.name_length_max;
        renamePanel.m_confirm.onClick.Add(CommitRename);
        renamePanel.m_panel.m_close.onClick.Set(CloseRename); // 关闭 / 取消 = 放弃修改
        renamePanel.m_cancel.onClick.Set(CloseRename);
    }

    private void OpenRename()
    {
        renamePanel.m_input.text = ClientSelection.playerName;
        GRoot.inst.AddChild(renamePanel); // 每次弹出都重新置顶
        renamePanel.visible = true;
        ApplyRenameTransform();
    }

    private void CloseRename() => renamePanel.visible = false;

    // 只有名字合法才改名并关闭；不合法只飘字，弹窗留在原地
    private void CommitRename()
    {
        int min = ConfigManager.GlobalVariableConfig.name_length_min;
        int max = ConfigManager.GlobalVariableConfig.name_length_max;
        string name = (renamePanel.m_input.text ?? "").Trim();
        if (name.Length < min || name.Length > max)
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText($"玩家名字必须为{min}-{max}个字符");
            return;
        }

        ClientSelection.playerName = name;
        if (Tool.SaveManager != null) Tool.SaveManager.playerName = name;
        RefreshPlayerName();
        renamePanel.visible = false;
    }

    private void RefreshPlayerName()
    {
        panel.m_playerInfo.m_label_playerName.text = ClientSelection.playerName;
    }

    // 与页面同比例缩放并居中（弹窗尺寸 400×300，自带全屏遮罩）
    private void ApplyRenameTransform()
    {
        if (renamePanel == null) return;
        renamePanel.scale = new Vector2(UiScale, UiScale);
        renamePanel.pivotAsAnchor = true;
        renamePanel.pivot = new Vector2(0.5f, 0.5f);
        renamePanel.SetXY(GRoot.inst.width * 0.5f, GRoot.inst.height * 0.5f);
    }

    private void BuildGuidePanel()
    {
        guidePanel = UI_GuidePanel.CreateInstance();
        guidePanel.opaque = true;
        GRoot.inst.AddChild(guidePanel);
        guidePanel.visible = false;
        guidePanel.m_btn_back.onClick.Set(CloseGuide);
        guidePanel.m_menu_btn_list.itemRenderer = RenderGuideMenu;
        guidePanel.m_menu_btn_list.numItems = GuideMenuTitles.Length;
        guidePanel.m_item_list.itemRenderer = RenderGuideItem;
        ApplyGuideTransform();
    }

    private void OpenGuide()
    {
        GRoot.inst.AddChild(guidePanel); // 每次弹出都重新置顶
        guidePanel.visible = true;
        ApplyGuideTransform();
        SwitchGuideTab(guideTab);
    }

    private void CloseGuide() => guidePanel.visible = false;

    private void ApplyGuideTransform()
    {
        if (guidePanel == null) return;
        guidePanel.scale = new Vector2(UiScale, UiScale);
        guidePanel.pivotAsAnchor = true;
        guidePanel.pivot = new Vector2(0.5f, 0.5f);
        guidePanel.SetXY(GRoot.inst.width * 0.5f, GRoot.inst.height * 0.5f);
    }

    private void RenderGuideMenu(int index, GObject obj)
    {
        var item = (UI_GuideMenuLabel)obj;
        item.m_title.text = GuideMenuTitles[index];
        item.touchable = true;
        item.onClick.Set(() => { });
        item.onClick.Add(() => SwitchGuideTab(index));
    }

    private void SwitchGuideTab(int tab)
    {
        guideTab = tab;
        guideEntries.Clear();
        guideEntries.AddRange(tab == 0 ? GuideCatalog.BuildSkillEntries() : GuideCatalog.BuildBuffEntries());
        guidePanel.m_item_list.numItems = guideEntries.Count;
    }

    private void RenderGuideItem(int index, GObject obj)
    {
        var item = (UI_GuideItem)obj;
        var entry = guideEntries[index];
        item.m_icon.texture = entry.icon != null ? new NTexture(entry.icon) : null;
        item.m_title.text = entry.title;
        item.m_des.text = entry.description;
    }

    // 列表是攻/守混排：0~5 进攻、6~11 防守，下标直接当存档下标用
    private static int CharacterCount => Config.attack_character_count + Config.defense_character_count;

    private static bool IsDefense(int saveIndex) => saveIndex >= Config.attack_character_count;

    // 当前展示的角色（攻/守共用一条列表）；两侧的选择各自记在 ClientSelection 里，上报时用到
    private int selectedSaveIndex;

    private void Refresh()
    {
        var selection = panel.m_character_selection_panel;
        selection.m_characterList.numItems = CharacterCount;
        selection.m_current_selected_camp.selectedIndex = IsDefense(selectedSaveIndex) ? 1 : 0;
        panel.m_character_attr_panel.m_character_selcted.selectedIndex = 1;
        RefreshAttrList();
        RefreshSpecialAbilities();
        RefreshCharacterInfo();
    }

    // 特殊能力：初始技能（攻守都有）+ 防守方被动（暂无配表，见 GuideCatalog）
    private void RefreshSpecialAbilities()
    {
        int saveIndex = selectedSaveIndex;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        var entries = GuideCatalog.BuildCharacterAbilities(info, saveIndex, IsDefense(saveIndex));

        var list = panel.m_character_attr_panel.m_special_ability_list;
        list.itemRenderer = (i, obj) =>
        {
            var item = (UI_HomeSpecialAbility)obj;
            item.m_label.text = $"{entries[i].title}：{entries[i].description}";
        };
        list.numItems = entries.Count;
    }

    private void RenderCharacter(int index, GObject obj)
    {
        var head = (UI_RoleHead)obj;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(index) : default;

        head.m_roleName.text = DisplayName(info, index);

        // 头像：图标列表未导入或下标越界时置空
        var icons = Tool.AssetsManager == null ? null
            : IsDefense(index) ? Tool.AssetsManager.DefenseCharacterIcons : Tool.AssetsManager.AttackCharacterIcons;
        int iconIndex = IsDefense(index) ? index - Config.attack_character_count : index;
        bool hasIcon = icons != null && iconIndex < icons.Count && icons[iconIndex] != null;
        head.m_headIcon.texture = hasIcon ? new NTexture(icons[iconIndex]) : null;
        head.m_lock.selectedIndex = 0; // 全角色默认解锁
        head.m_confirm.selectedIndex = index == selectedSaveIndex ? 1 : 0;

        // 点击选中（FGUI onClick 为追加制，先清后加以防列表复用叠加）
        head.onClick.Set(() => { });
        head.onClick.Add(() =>
        {
            Select(index);
        });
    }

    private void Select(int saveIndex)
    {
        if (IsDefense(saveIndex)) ClientSelection.selectedDefenseIndex = saveIndex - Config.attack_character_count;
        else ClientSelection.selectedAttackIndex = saveIndex;
        selectedSaveIndex = saveIndex;
        Refresh();
        RefreshPreview();
    }

    private void ConfirmSelection()
    {
        if (Tool.UIManager != null)
            Tool.UIManager.ShowFlyText($"已选择 {panel.m_label_character_name.text}");
    }

    private void RefreshAttrList()
    {
        int saveIndex = selectedSaveIndex;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        int[] values = ToValues(info.GetAttribute());

        panel.m_character_attr_panel.m_attrList.itemRenderer = (i, obj) =>
        {
            var item = (UI_AttrItem)obj;
            item.m_attrName.text = StatKeys[i];
            item.m_attrValue.text = values[i] + AttrSuffixes[i];
            FillAttrBar(item.m_progressBar, i, values[i]);
        };
        panel.m_character_attr_panel.m_attrList.numItems = StatKeys.Length;
    }

    // 属性条 = 固定 10 格：按 attr_range.csv 的区间把数值映射成点亮格数（非零至少亮 1 格）
    private static void FillAttrBar(GList bar, int attrIndex, int value)
    {
        if (bar == null) return;
        var range = ConfigManager.AttrRangeConfig.Get(attrIndex);
        int filled = Mathf.Clamp(Mathf.CeilToInt(range.Ratio(value) * AttrBarSegments), 0, AttrBarSegments);

        bar.itemRenderer = (i, obj) =>
        {
            if (obj is UI_AttrValueItem cell && cell.m_filled != null) cell.m_filled.selectedIndex = i < filled ? 1 : 0;
        };
        bar.numItems = AttrBarSegments;
    }

    private void RefreshCharacterInfo()
    {
        int saveIndex = selectedSaveIndex;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        int iconIndex = IsDefense(saveIndex) ? saveIndex - Config.attack_character_count : saveIndex;
        panel.m_label_character_name.text = DisplayName(info, iconIndex);
    }

    // 列表条目与右侧标题共用一个名字口径：配表没名字时退回「进攻/防守角色 + 页内下标」
    private static string DisplayName(ConfigManager.EntityConfig info, int saveIndex)
    {
        if (!string.IsNullOrEmpty(info.name)) return info.name;
        return IsDefense(saveIndex) ? $"防守角色 {saveIndex - Config.attack_character_count}" : $"进攻角色 {saveIndex}";
    }

    // 属性一律取整显示
    private static int[] ToValues(EntityAttribute attr)
    {
        return new[]
        {
            Mathf.RoundToInt(attr.health), Mathf.RoundToInt(attr.attack), Mathf.RoundToInt(attr.critRate),
            Mathf.RoundToInt(attr.critDamage), Mathf.RoundToInt(attr.viewDistance), Mathf.RoundToInt(attr.weaponSlotCount),
        };
    }

    // 选中角色变化后重建场景里的预览模型
    private void RefreshPreview()
    {
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.RefreshHome(ClientSelection.selectedAttackIndex, ClientSelection.selectedDefenseIndex);
    }

    private async void OnConnectClicked()
    {
        // 连接前校验玩家名字长度，不合法则飘字提示并拦截连接
        int nameMin = ConfigManager.GlobalVariableConfig.name_length_min;
        int nameMax = ConfigManager.GlobalVariableConfig.name_length_max;
        string name = (ClientSelection.playerName ?? "").Trim();
        if (name.Length < nameMin || name.Length > nameMax)
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText($"玩家名字必须为{nameMin}-{nameMax}个字符");
            return;
        }
        if (name != ClientSelection.playerName)
        {
            // 首尾空白去掉后回写：上报、显示与存档保持同一口径
            ClientSelection.playerName = name;
            if (Tool.SaveManager != null) Tool.SaveManager.playerName = name;
            RefreshPlayerName();
        }
        if (Tool.NetworkManager == null) return;
        var result = await Tool.NetworkManager.TryConnect(panel.m_connectPanel.m_input_ipaddress.m_content.text);
        if (result != NetworkManager.ConnectResult.Success) return;
        Tool.NetworkManager.EnterWorld();
    }
    #endregion
}
