using System.Collections.Generic;
using System.Threading.Tasks;
using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

using Ros.Config;
// 首页：攻/守分页角色列表 + 属性面板 + 改名弹窗 + 连接
public class HomePage : PageBase
{
    private static readonly message_variablesEnum[] StatNameKeys =
    {
        message_variablesEnum.home_attrName_health, message_variablesEnum.home_attrName_attack,
        message_variablesEnum.home_attrName_crit, message_variablesEnum.home_attrName_critDamage,
        message_variablesEnum.home_attrName_view, message_variablesEnum.home_attrName_skillSlot,
    };
    private static readonly message_variablesEnum[] StatSuffixKeys = // 与 StatNameKeys 一一对应
    {
        message_variablesEnum.home_attrSuffixes_health, message_variablesEnum.home_attrSuffixes_attack,
        message_variablesEnum.home_attrSuffixes_crit, message_variablesEnum.home_attrSuffixes_critDamage,
        message_variablesEnum.home_attrSuffixes_view, message_variablesEnum.home_attrSuffixes_skillSlot,
    };
    private const int AttrBarSegments = 10; // 属性条格数（UI_AttrItem.m_progressBar 里的 UI_AttrValueItem 数量）
    private const int AttrIndexHealth = 0;         // 属性列表里生命的下标
    private const int RangeKeyHealthAttack = 6;    // attr_range：进攻方生命（两阵营血量量级不同，分开取区间）
    private const int RangeKeyHealthDefense = 7;   // attr_range：防守方生命

    private readonly UI_HomePanel panel;
    private UI_RemanePanel renamePanel; // 改名弹窗：设计自带全屏遮罩，挂 GRoot 置顶

    public HomePage(UI_HomePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    protected override UiConfig InitConfig() => new UiConfig
    {
        Order = UILayer.Page,
        ShowBlackMask = false,
        ClickOutsideToClose = false,
    };

    protected override void Init()
    {
        var selection = panel.m_character_selection_panel;
        selection.m_characterList.itemRenderer = RenderCharacter;
        selection.m_tab_attacker.onClick.Add(() => SwitchCamp(false));
        selection.m_tab_defenser.onClick.Add(() => SwitchCamp(true));

        // 玩家改名：按钮与名字都可点
        panel.m_playerInfo.m_label_playerName.touchable = true;
        panel.m_playerInfo.m_label_playerName.onClick.Add(OpenRename);
        panel.m_playerInfo.m_btn_editName.onClick.Add(OpenRename);
        BuildRenamePanel();

        panel.m_character_attr_panel.m_btn_select_character.onClick.Add(ConfirmSelection);

        panel.m_btn_settings.onClick.Add(OpenSettings);

        panel.m_connectPanel.m_btn_connect.title = MessageVariable.V(message_variablesEnum.home_btnConnect);
        panel.m_connectPanel.m_btn_connect.onClick.Add(OnConnectClicked);
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        Tool.EnvironmentManager.SetTime(GlobalConfig.home_env_time); // 固定首页昼夜，不沿用对局结束时的时刻
        // 每次进首页固定从进攻方已选角色开始展示，不沿用上次浏览到的角色
        defenseTab = false;
        selectedSaveIndex = ClientSelection.selectedAttackIndex;
        RefreshPlayerName();
        Refresh();
        RefreshPreview();
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

        renamePanel.m_input.maxLength = GlobalVariable.I(global_variablesEnum.name_length_max);
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

    // 玩家名唯一的落盘入口：改名弹窗、连接前校验、AI 指令都走这里
    // 返回 false 表示长度不合法，min/max 是配表区间，供调用方提示
    public bool TrySetPlayerName(string rawName, out int min, out int max)
    {
        min = GlobalVariable.I(global_variablesEnum.name_length_min);
        max = GlobalVariable.I(global_variablesEnum.name_length_max);
        string name = (rawName ?? "").Trim();
        if (name.Length < min || name.Length > max) return false;

        ClientSelection.playerName = name;
        if (Tool.SaveManager != null) Tool.SaveManager.playerName = name;
        RefreshPlayerName();
        return true;
    }

    // 只有名字合法才改名并关闭；不合法只飘字，弹窗留在原地
    private void CommitRename()
    {
        if (!TrySetPlayerName(renamePanel.m_input.text, out int min, out int max))
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.name_length_invalid, min, max);
            return;
        }
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

    // 设置面板是全局子界面，三个页面共用（UIManager 持有）
    private static void OpenSettings()
    {
        if (Tool.UIManager != null) Tool.UIManager.OpenSettings();
    }

    // 存档下标：0~5 进攻、6~11 防守；页签只列其中一侧
    private static bool IsDefense(int saveIndex) => saveIndex >= GlobalConfig.attack_character_count;

    private bool defenseTab;

    // 当前浏览的角色（仅本页内存状态，不进存档、不进 ClientSelection）
    private int selectedSaveIndex;

    private int CampOffset => defenseTab ? GlobalConfig.attack_character_count : 0;

    // 本阵营已选角色的存档下标
    private int ChosenSaveIndex => CampOffset + (defenseTab ? ClientSelection.selectedDefenseIndex : ClientSelection.selectedAttackIndex);

    private void Refresh()
    {
        var selection = panel.m_character_selection_panel;
        int count = defenseTab ? GlobalConfig.defense_character_count : GlobalConfig.attack_character_count;
        selection.m_current_selected_camp.selectedIndex = defenseTab ? 1 : 0;
        selection.m_tab_attacker.selected = !defenseTab;
        selection.m_tab_defenser.selected = defenseTab;
        selection.m_characterList.numItems = count;
        // numItems 不变时 FGUI 不重绘已有条目，切页签与选中变化都要手动重画
        for (int i = 0; i < count; i++) RenderCharacter(i, selection.m_characterList.GetChildAt(i));

        panel.m_character_attr_panel.m_character_selcted.selectedIndex = selectedSaveIndex == ChosenSaveIndex ? 1 : 0;
        RefreshAttrList();
        RefreshSpecialAbilities();
        RefreshCharacterInfo();
    }

    // 切阵营后回到该阵营已选角色
    private void SwitchCamp(bool defense)
    {
        if (defenseTab == defense) return;
        defenseTab = defense;
        Browse(ChosenSaveIndex);
    }

    // 特殊能力：初始技能 + 被动描述，均读配表（见 GuideCatalog）
    private void RefreshSpecialAbilities()
    {
        int saveIndex = selectedSaveIndex;
        entity info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        var entries = GuideCatalog.BuildCharacterAbilities(info);

        var list = panel.m_character_attr_panel.m_special_ability_list;
        list.itemRenderer = (i, obj) =>
        {
            var item = (UI_HomeSpecialAbility)obj;
            item.m_label.text = string.IsNullOrEmpty(entries[i].description)
                ? entries[i].title
                : $"{entries[i].title}：{entries[i].description}";
        };
        list.numItems = entries.Count;
    }

    private void RenderCharacter(int index, GObject obj)
    {
        var head = (UI_RoleHead)obj;
        int saveIndex = CampOffset + index;
        entity info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;

        head.m_roleName.text = DisplayName(info, saveIndex);

        // 头像：图标列表未导入或下标越界时置空
        var icons = Tool.ResourceManager == null ? null
            : defenseTab ? Tool.ResourceManager.DefenseCharacterIcons : Tool.ResourceManager.AttackCharacterIcons;
        bool hasIcon = icons != null && index < icons.Count && icons[index] != null;
        head.m_headIcon.texture = hasIcon ? new NTexture(icons[index]) : null;
        head.m_lock.selectedIndex = 0; // 全角色默认解锁
        head.m_confirm.selectedIndex = saveIndex == ChosenSaveIndex ? 1 : 0;

        // 点击浏览（FGUI onClick 为追加制，先清后加以防列表复用叠加）
        head.onClick.Set(() => { });
        head.onClick.Add(() => Browse(saveIndex));
    }

    // 只切换展示与预览，不写出场选择；确认按钮才落 ClientSelection
    private void Browse(int saveIndex)
    {
        selectedSaveIndex = saveIndex;
        Refresh();
        RefreshPreview();
    }

    private void ConfirmSelection()
    {
        int localIndex = selectedSaveIndex - CampOffset;
        if (defenseTab) ClientSelection.selectedDefenseIndex = localIndex;
        else ClientSelection.selectedAttackIndex = localIndex;
        Refresh();
        if (Tool.UIManager != null)
            Tool.UIManager.ShowFlyText(promptEnum.character_selected, panel.m_label_character_name.text);
    }

    // 出场选择唯一的落盘入口：首页「确认选择」与 AI 指令都走这里
    // 先切到该阵营并浏览到该角色（刷新列表 / 属性 / 场景预览），再落 ClientSelection
    public void SelectCharacter(bool defense, int index)
    {
        defenseTab = defense;
        Browse(CampOffset + index);
        ConfirmSelection();
    }

    private void RefreshAttrList()
    {
        int saveIndex = selectedSaveIndex;
        entity info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        int[] values = ToValues(info.GetAttribute());
        bool defense = IsDefense(saveIndex);

        panel.m_character_attr_panel.m_attrList.itemRenderer = (i, obj) =>
        {
            var item = (UI_AttrItem)obj;
            item.m_attrName.text = MessageVariable.V(StatNameKeys[i]);
            item.m_attrValue.text = values[i] + MessageVariable.V(StatSuffixKeys[i]);
            FillAttrBar(item.m_progressBar, RangeKey(i, defense), values[i]);
        };
        panel.m_character_attr_panel.m_attrList.numItems = StatNameKeys.Length;
    }

    // 生命按阵营取各自区间，其余属性与 attr_range 行号一致
    private static int RangeKey(int attrIndex, bool defense)
    {
        if (attrIndex != AttrIndexHealth) return attrIndex;
        return defense ? RangeKeyHealthDefense : RangeKeyHealthAttack;
    }

    // 属性条 = 固定 10 格：按 attr_range.csv 的区间把数值映射成点亮格数（非零至少亮 1 格）
    private static void FillAttrBar(GList bar, int rangeKey, int value)
    {
        if (bar == null) return;
        var range = AttrRangeConfig.Get(rangeKey);
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
        entity info = Tool.InfoManager != null ? Tool.InfoManager.GetPlayerCharacterInfo(saveIndex) : default;
        panel.m_label_character_name.text = DisplayName(info, saveIndex);
    }

    // 列表条目与右侧标题共用一个名字口径：配表没名字时退回「进攻/防守角色 + 阵营内下标」
    private static string DisplayName(entity info, int saveIndex)
    {
        if (!string.IsNullOrEmpty(info.name)) return info.name;
        return IsDefense(saveIndex)
            ? MessageVariable.F(message_variablesEnum.home_roleName_defense, saveIndex - GlobalConfig.attack_character_count)
            : MessageVariable.F(message_variablesEnum.home_roleName_attack, saveIndex);
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

    // 浏览角色变化后重建场景里的预览模型
    private void RefreshPreview()
    {
        if (Tool.ClientLogicManager == null || Tool.ClientLogicManager.HomePreview == null) return;
        int saveIndex = selectedSaveIndex;
        var type = IsDefense(saveIndex)
            ? EntityType.Defense(saveIndex - GlobalConfig.attack_character_count)
            : EntityType.Attack(saveIndex);
        Tool.ClientLogicManager.HomePreview.RefreshHome(type);
    }

    private async void OnConnectClicked()
    {
        // 连接前校验玩家名字长度，不合法则飘字提示并拦截连接
        if (!TrySetPlayerName(ClientSelection.playerName, out int min, out int max))
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.name_length_invalid, min, max);
            return;
        }
        await ConnectAsync(panel.m_connectPanel.m_input_ipaddress.m_content.text);
    }

    // 连接唯一的发起入口：连接按钮与 AI 指令共用（名字校验由调用方先走 TrySetPlayerName）
    public async Task<NetworkManager.ConnectResult> ConnectAsync(string ip)
    {
        if (Tool.NetworkManager == null)
            throw new System.InvalidOperationException("[首页] 场景缺少 NetworkManager，无法连接");

        var result = await Tool.NetworkManager.TryConnect(ip);
        if (result != NetworkManager.ConnectResult.Success) return result;
        Tool.NetworkManager.EnterWorld();
        return result;
    }
    #endregion
}
