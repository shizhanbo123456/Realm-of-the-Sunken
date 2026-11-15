/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SettingScrollView : GComponent
    {
        public GButton m_toggle_caustic;
        public GButton m_toggle_camea_shake;
        public GButton m_toggle_third_person_perspctivee;
        public GButton m_toggle_auto_pitch;
        public GList m_rule_buttons;
        public GTextField m_rule_content;
        public GButton m_back_to_fight;
        public GButton m_exit;
        public const string URL = "ui://q68vr2bfkqn8rq";

        public static UI_SettingScrollView CreateInstance()
        {
            return (UI_SettingScrollView)UIPackage.CreateObject("Main", "SettingScrollView");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_toggle_caustic = (GButton)GetChildAt(3);
            m_toggle_camea_shake = (GButton)GetChildAt(5);
            m_toggle_third_person_perspctivee = (GButton)GetChildAt(7);
            m_toggle_auto_pitch = (GButton)GetChildAt(9);
            m_rule_buttons = (GList)GetChildAt(13);
            m_rule_content = (GTextField)GetChildAt(14);
            m_back_to_fight = (GButton)GetChildAt(15);
            m_exit = (GButton)GetChildAt(16);
        }
    }
}