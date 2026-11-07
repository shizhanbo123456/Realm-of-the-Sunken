using Ros.UI.Main;
using UnityEngine;

public class HomePage : PageBase
{
    private readonly UI_HomePanel panel;

    public HomePage(UI_HomePanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override void Construct()
    {
        // 角色选择浮层自带页签/列表/属性表/关闭按钮，选角、切页签、关闭三种回调分别回来做场景预览与相机转向
        panel.m_characterList.Build(RefreshPreview, TurnToCamp, CloseCharacterList);
        panel.m_btn_select_character.onClick.Add(OpenCharacterList);

        var nameInput = panel.m_playerInfo.m_input_playerName;
        nameInput.maxLength = ConfigManager.GlobalVariableConfig.name_length_max;
        nameInput.text = ClientSelection.playerName;
        nameInput.onChanged.Add(() =>
        {
            ClientSelection.playerName = nameInput.text;
            if (Tool.SaveManager == null) return;
            // 只有长度合法才落盘：空/超长名字写进存档会让下次启动判为无效存档并重建（等级经验一起丢）
            string trimmed = nameInput.text != null ? nameInput.text.Trim() : "";
            if (trimmed.Length < ConfigManager.GlobalVariableConfig.name_length_min
                || trimmed.Length > ConfigManager.GlobalVariableConfig.name_length_max) return;
            Tool.SaveManager.playerName = trimmed;
            Tool.SaveManager.Save();
        });

        panel.m_connectPanel.m_btn_connect.title = "连接";
        panel.m_connectPanel.m_btn_connect.onClick.Add(OnConnectClicked);
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        panel.m_playerInfo.m_input_playerName.text = ClientSelection.playerName;
        RefreshPlayerExp();
        panel.m_show_character_panel.selectedIndex = 0; // 每次进入首页收起角色选择浮层
        panel.m_characterList.Refresh();                // 列表与属性表按当前存档重画
        RefreshPreview();                               // 当前选中的两角色摆到场景锚点
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetHomeView();
    }

    #region//Local
    private void OpenCharacterList()
    {
        panel.m_show_character_panel.selectedIndex = 1;
        panel.m_characterList.Open(); // 同步页签并让相机转向当前页签的角色
    }

    private void CloseCharacterList()
    {
        panel.m_show_character_panel.selectedIndex = 0;
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetHomeView();
    }

    private void TurnToCamp(bool defense)
    {
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.LookAtHome(defense ? PlayerCampType.Defense : PlayerCampType.Attack);
    }

    // 选中角色变化后重建场景里的预览模型
    private void RefreshPreview()
    {
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.RefreshHome(ClientSelection.selectedAttackIndex, ClientSelection.selectedDefenseIndex);
    }

    // 玩家经验条：进度 = 当前级经验 / 升到下一级所需（经验与等级都在存档里，满级拉满）
    private void RefreshPlayerExp()
    {
        var info = panel.m_playerInfo;
        int level = Tool.SaveManager != null ? Tool.SaveManager.playerLevel : 1;
        int exp = Tool.SaveManager != null ? Tool.SaveManager.playerExp : 0;
        bool maxed = level >= Config.player_max_level;
        int need = maxed ? 0 : Config.GetPlayerLevelUpExp(level);
        info.m_bar_fill_energy.fillAmount = need > 0 ? Mathf.Clamp01((float)exp / need) : 1f;
        info.m_label_exp.text = need > 0 ? $"{exp}/{need}" : "MAX";
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
            panel.m_playerInfo.m_input_playerName.text = name;
            if (Tool.SaveManager != null)
            {
                Tool.SaveManager.playerName = name;
                Tool.SaveManager.Save();
            }
        }
        if (Tool.NetworkManager == null) return;
        var result = await Tool.NetworkManager.TryConnect(panel.m_connectPanel.m_input_ipaddress.m_content.text);
        if (result != NetworkManager.ConnectResult.Success) return;
        Tool.NetworkManager.EnterWorld();
    }
    #endregion
}
