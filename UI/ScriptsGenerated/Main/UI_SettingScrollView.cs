/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SettingScrollView : GComponent
    {
        public Controller m_in_server;
        public GButton m_toggle_caustic;
        public GButton m_toggle_volumetric_light;
        public GButton m_toggle_third_person_perspctivee;
        public GButton m_toggle_auto_pitch;
        public GButton m_toggle_camera_active_with_focus;
        public GButton m_toggle_hide_bar;
        public GButton m_toggle_show_event;
        public GButton m_toggle_show_damage_label;
        public GButton m_open_rule;
        public GButton m_open_guide;
        public GButton m_open_bridge;
        public GButton m_back_to_fight;
        public GButton m_exit;
        public GButton m_back;
        public const string URL = "ui://q68vr2bfkqn8rq";

        public static UI_SettingScrollView CreateInstance()
        {
            return (UI_SettingScrollView)UIPackage.CreateObject("Main", "SettingScrollView");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_in_server = GetControllerAt(0);
            m_toggle_caustic = (GButton)GetChildAt(3);
            m_toggle_volumetric_light = (GButton)GetChildAt(5);
            m_toggle_third_person_perspctivee = (GButton)GetChildAt(7);
            m_toggle_auto_pitch = (GButton)GetChildAt(9);
            m_toggle_camera_active_with_focus = (GButton)GetChildAt(11);
            m_toggle_hide_bar = (GButton)GetChildAt(13);
            m_toggle_show_event = (GButton)GetChildAt(15);
            m_toggle_show_damage_label = (GButton)GetChildAt(17);
            m_open_rule = (GButton)GetChildAt(21);
            m_open_guide = (GButton)GetChildAt(23);
            m_open_bridge = (GButton)GetChildAt(25);
            m_back_to_fight = (GButton)GetChildAt(28);
            m_exit = (GButton)GetChildAt(29);
            m_back = (GButton)GetChildAt(30);
        }
    }
}