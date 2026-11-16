/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_CardGraphic : GComponent
    {
        public Controller m_level;
        public Controller m_show_skill_display;
        public GTextField m_title;
        public GTextField m_des;
        public GButton m_btn_select;
        public UI_CardSkillDisplay m_skillpreview;
        public const string URL = "ui://q68vr2bflwt1q1";

        public static UI_CardGraphic CreateInstance()
        {
            return (UI_CardGraphic)UIPackage.CreateObject("Main", "CardGraphic");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_level = GetControllerAt(0);
            m_show_skill_display = GetControllerAt(1);
            m_title = (GTextField)GetChildAt(3);
            m_des = (GTextField)GetChildAt(4);
            m_btn_select = (GButton)GetChildAt(5);
            m_skillpreview = (UI_CardSkillDisplay)GetChildAt(6);
        }
    }
}