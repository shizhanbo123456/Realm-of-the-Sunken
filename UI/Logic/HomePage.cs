using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

public class HomePage : PageBase
{
    private static readonly string[] StatKeys = { "生命", "攻击", "暴击", "暴伤", "视野", "技能槽" };
    private static readonly string[] AttrSuffixes = { "", "", "%", "%", "m", "" }; // 与 StatKeys 一一对应
    private const string GainGreen = "#6BD98C";    // 已取得的累计增益
    private const string NextGainOrange = "#FF9E47"; // 下一级将获得的增益

    private readonly UI_HomePanel panel;
    private UI_RemanePanel renamePanel; // 改名弹窗：设计自带全屏遮罩，挂 GRoot 置顶
    private bool defenseTab;

    public HomePage(UI_HomePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override void Construct()
    {
        // 角色列表/攻守页签/属性表/选中角色经验条都常驻首页，没有浮层与开关
        panel.m_characterList.itemRenderer = RenderCharacter;
        panel.m_btn_attack.onClick.Add(() => SwitchTab(false));
        panel.m_btn_defense.onClick.Add(() => SwitchTab(true));
        ApplyTabMark();

        panel.m_playerInfo.m_remame_button.onClick.Add(OpenRename);
        BuildRenamePanel();

        panel.m_connectPanel.m_btn_connect.title = "连接";
        panel.m_connectPanel.m_btn_connect.onClick.Add(OnConnectClicked);
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        RefreshPlayerName();
        RefreshPlayerExp();
        Refresh();                                      // 列表、属性表与角色经验条按当前存档重画
        RefreshPreview();                               // 当前选中的两角色摆到场景锚点
        if (renamePanel != null) renamePanel.visible = false;
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetHomeView();
    }

    public override void Exit()
    {
        base.Exit();
        if (renamePanel != null) renamePanel.visible = false; // 弹窗挂 GRoot，不收起来会留在别的页面上
    }

    public override void OnResize(float width, float height)
    {
        base.OnResize(width, height);
        ApplyRenameTransform();
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
        renamePanel.m_panel.m_close.onClick.Set(CloseRename); // 关闭 = 放弃修改
    }

    private void OpenRename()
    {
        renamePanel.m_input.text = ClientSelection.playerName;
        GRoot.inst.AddChild(renamePanel); // 每次弹出都重新置顶
        renamePanel.visible = true;
        ApplyRenameTransform();
    }

    private void CloseRename()
    {
        renamePanel.visible = false;
    }

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

    private void SwitchTab(bool defense)
    {
        defenseTab = defense;
        ApplyTabMark();
        Refresh();
    }

    private void ApplyTabMark()
    {
        panel.m_btn_attack.selected = !defenseTab;
        panel.m_btn_defense.selected = defenseTab;
    }

    private int SelectedIndex => defenseTab ? ClientSelection.selectedDefenseIndex : ClientSelection.selectedAttackIndex;

    private int SelectedSaveIndex => defenseTab ? Config.attack_character_count + SelectedIndex : SelectedIndex;

    // 列表、属性表、选中角色信息同属「当前选中角色」，一起重画
    private void Refresh()
    {
        panel.m_characterList.numItems = defenseTab ? Config.defense_character_count : Config.attack_character_count;
        RefreshAttrList();
        RefreshCharacterInfo();
    }

    private void RenderCharacter(int index, GObject obj)
    {
        var head = (UI_RoleHead)obj;
        int saveIndex = defenseTab ? Config.attack_character_count + index : index;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        int level = Tool.SaveManager != null ? Tool.SaveManager.GetCharacterLevel(saveIndex) : 1;
        bool unlocked = Tool.SaveManager == null || Tool.SaveManager.IsCharacterUnlocked(saveIndex);

        head.m_roleName.text = DisplayName(info, index);
        head.m_level.selectedIndex = Mathf.Clamp(level - 1, 0, head.m_level.pageCount - 1);
        head.m_lock.selectedIndex = unlocked ? 0 : 1;
        head.m_quality.selectedIndex = info.quality;
        head.enabled = unlocked;

        // 头像：图标列表未导入或下标越界时置空
        var icons = Tool.AssetsManager == null ? null
            : defenseTab ? Tool.AssetsManager.DefenseCharacterIcons : Tool.AssetsManager.AttackCharacterIcons;
        bool hasIcon = icons != null && index < icons.Count && icons[index] != null;
        head.m_headIcon.texture = hasIcon ? new NTexture(icons[index]) : null;

        // 点击选中（FGUI onClick 为追加制，先清后加以防列表复用叠加）
        head.onClick.Set(() => { });
        head.onClick.Add(() =>
        {
            if (defenseTab) ClientSelection.selectedDefenseIndex = index;
            else ClientSelection.selectedAttackIndex = index;
            Refresh();
            RefreshPreview();
        });
    }

    private void RefreshAttrList()
    {
        int saveIndex = SelectedSaveIndex;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        int level = Tool.SaveManager != null ? Tool.SaveManager.GetCharacterLevel(saveIndex) : 1;

        int[] baseValues = ToValues(info.GetAttribute(1));      // 基础：1 级属性
        int[] curValues = ToValues(info.GetAttribute(level));   // 当前等级加成后
        var nextAttr = level < Config.save_max_level ? info.GetAttribute(level + 1) : null;
        int[] nextValues = nextAttr != null ? ToValues(nextAttr) : curValues; // 满级没有下一级：条与当前一致

        string[] values = new string[StatKeys.Length];
        for (int i = 0; i < StatKeys.Length; i++)
        {
            int? next = nextAttr != null ? nextValues[i] : (int?)null;
            values[i] = StatPart(curValues[i], baseValues[i], next, AttrSuffixes[i]);
        }

        panel.m_attrList.itemRenderer = (i, obj) =>
        {
            var item = (UI_AttrItem)obj;
            var range = ConfigManager.AttrRangeConfig.Get(i); // 属性条比例：最小值=空、最大值=满
            item.m_attrName.text = StatKeys[i];
            item.m_attrValue.text = values[i];
            item.m_base.fillAmount = range.Ratio(baseValues[i]);
            item.m_growth_fill.fillAmount = range.Ratio(curValues[i]);
            item.m_next_level_fill.fillAmount = range.Ratio(nextValues[i]);
        };
        panel.m_attrList.numItems = StatKeys.Length;
    }

    // 右侧角色信息：名称 + 经验条（进度 = 当前级经验 / 升到下一级所需，满级拉满）
    private void RefreshCharacterInfo()
    {
        int saveIndex = SelectedSaveIndex;
        ConfigManager.EntityConfig info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        int level = Tool.SaveManager != null ? Tool.SaveManager.GetCharacterLevel(saveIndex) : 1;
        int exp = Tool.SaveManager != null ? Tool.SaveManager.GetCharacterExp(saveIndex) : 0;
        bool maxed = level >= Config.save_max_level;
        int need = maxed ? 0 : ConfigManager.ExpCharacterConfig.Get(level + 1).exp;

        panel.m_label_character_name.text = DisplayName(info, SelectedIndex);
        panel.m_bar_fill_character_exp.fillAmount = need > 0 ? Mathf.Clamp01((float)exp / need) : 1f;
        panel.m_label_character_exp.text = need > 0 ? $"{exp}/{need}" : "MAX";
    }

    // 列表条目与右侧标题共用一个名字口径：配表没名字时退回「进攻/防守角色 + 页签内下标」
    private string DisplayName(ConfigManager.EntityConfig info, int index)
    {
        if (!string.IsNullOrEmpty(info.name)) return info.name;
        return defenseTab ? $"防守角色 {index}" : $"进攻角色 {index}";
    }

    // 属性一律取整显示（配置里的成长系数会算出小数，界面上不出现小数）
    private static int[] ToValues(EntityAttribute attr)
    {
        return new[]
        {
            Mathf.RoundToInt(attr.health), Mathf.RoundToInt(attr.attack), Mathf.RoundToInt(attr.critRate),
            Mathf.RoundToInt(attr.critDamage), Mathf.RoundToInt(attr.viewDistance), Mathf.RoundToInt(attr.weaponSlotCount),
        };
    }

    private static string StatPart(int current, int baseVal, int? nextVal, string suffix = "")
    {
        string s = current + suffix;
        int gain = current - baseVal;
        if (gain != 0)
            s += $"[color={GainGreen}]+{gain}{suffix}[/color]";
        if (nextVal.HasValue)
        {
            int next = nextVal.Value - current;
            if (next != 0)
                s += $"[color={NextGainOrange}]（+{next}{suffix}）[/color]";
        }
        return s;
    }

    // 选中角色变化后重建场景里的预览模型
    private void RefreshPreview()
    {
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.RefreshHome(ClientSelection.selectedAttackIndex, ClientSelection.selectedDefenseIndex);
    }

    // 玩家经验条：等级 + 进度（当前级经验 / 升到下一级所需，满级拉满）
    private void RefreshPlayerExp()
    {
        var info = panel.m_playerInfo;
        int level = Tool.SaveManager != null ? Tool.SaveManager.playerLevel : 1;
        int exp = Tool.SaveManager != null ? Tool.SaveManager.playerExp : 0;
        bool maxed = level >= Config.player_max_level;
        int need = maxed ? 0 : Config.GetPlayerLevelUpExp(level);
        info.m_bar_fill_energy.fillAmount = need > 0 ? Mathf.Clamp01((float)exp / need) : 1f;
        info.m_label_exp.text = need > 0 ? $"等级 {level}   {exp}/{need}" : $"等级 {level}   MAX";
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
