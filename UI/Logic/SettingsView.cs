using FairyGUI;
using Ros.UI.Main;
using Ros.Config;
using UnityEngine;

// 设置子界面：三个页面共用同一份，显隐由 UIManager 统一管理；勾选态不会丢
public class SettingsView : PageBase
{
    // 由 UIManager 注入：这四个动作跨页面，去哪由它决定
    public System.Action OnOpenRule;
    public System.Action OnOpenGuide;
    public System.Action OnOpenBridge;
    public System.Action OnExit;

    private UI_SettingsPanel panel;

    private UI_SettingScrollView View => panel != null ? panel.m_settings.GetChildAt(0) as UI_SettingScrollView : null;

    public bool IsOpen => IsVisible;

    public SettingsView(UI_SettingsPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.settings;

    protected override void Init()
    {
        panel.m_settings.numItems = 1; // 容器是列表，唯一元素即真正的设置面板
        panel.opaque = true;
        panel.touchable = true;

        panel.m_close2.onClick.Set(CloseSelf);        // 关闭按钮（右上）
        panel.m_close.onClick.Set(CloseSelf);         // 关闭按钮（面板内）
        panel.m_panel.m_close.onClick.Set(CloseSelf); // 底框关闭按钮
        View.m_back_to_fight.onClick.Set(CloseSelf);
        View.m_back.onClick.Set(CloseSelf);
        View.m_exit.onClick.Set(() => OnExit?.Invoke());
        View.m_open_rule.onClick.Set(() => OnOpenRule?.Invoke());
        View.m_open_guide.onClick.Set(() => OnOpenGuide?.Invoke());
        View.m_open_bridge.onClick.Set(() => OnOpenBridge?.Invoke());

        BindToggles();
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        View.m_in_server.selectedIndex = NetworkManager.IsConnected ? 1 : 0; // 0 未连服务器 / 1 已连服务器
        SyncToggles();
    }

    private static void CloseSelf()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.settings);
    }

    // C 键切视角会写回同一个开关，面板常驻不重建，这里每帧把勾选态跟过去
    public override void Update(float deltaTime)
    {
        if (IsOpen) SyncToggles();
    }

    #region//Local
    // 四个勾选全量写 ClientSettings 并随存档落盘；勾选态与运行时是否已接无关
    private void BindToggles()
    {
        View.m_toggle_caustic.selected = ClientSettings.Caustics;
        View.m_toggle_third_person_perspctivee.selected = ClientSettings.ThirdPersonCamera;
        View.m_toggle_auto_pitch.selected = ClientSettings.AutoPitch;
        View.m_toggle_camea_shake.selected = ClientSettings.CameraShake;
        View.m_toggle_caustic.onChanged.Add(() =>
            ClientSettings.Caustics = View.m_toggle_caustic.selected);
        View.m_toggle_third_person_perspctivee.onChanged.Add(() =>
            ClientSettings.ThirdPersonCamera = View.m_toggle_third_person_perspctivee.selected);
        View.m_toggle_auto_pitch.onChanged.Add(() =>
            ClientSettings.AutoPitch = View.m_toggle_auto_pitch.selected);
        View.m_toggle_camea_shake.onChanged.Add(() =>
            ClientSettings.CameraShake = View.m_toggle_camea_shake.selected);
    }

    private void SyncToggles()
    {
        if (View == null) return;
        if (View.m_toggle_caustic.selected != ClientSettings.Caustics)
            View.m_toggle_caustic.selected = ClientSettings.Caustics;
        if (View.m_toggle_third_person_perspctivee.selected != ClientSettings.ThirdPersonCamera)
            View.m_toggle_third_person_perspctivee.selected = ClientSettings.ThirdPersonCamera;
        if (View.m_toggle_auto_pitch.selected != ClientSettings.AutoPitch)
            View.m_toggle_auto_pitch.selected = ClientSettings.AutoPitch;
        if (View.m_toggle_camea_shake.selected != ClientSettings.CameraShake)
            View.m_toggle_camea_shake.selected = ClientSettings.CameraShake;
    }
    #endregion
}
