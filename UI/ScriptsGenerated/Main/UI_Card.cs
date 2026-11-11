/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_Card : GComponent
    {
        public Controller m_show_skill_display;
        public UI_CardGraphic m_cardGraphic;
        public GTextField m_title;
        public GTextField m_des;
        public GButton m_btn_select;
        public UI_CardSkillDisplay m_skillpreview;
        public Transition m_show;
        public Transition m_hide;
        public const string URL = "ui://q68vr2bflwt1q2";

        public static UI_Card CreateInstance()
        {
            return (UI_Card)UIPackage.CreateObject("Main", "Card");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_show_skill_display = GetControllerAt(0);
            m_cardGraphic = (UI_CardGraphic)GetChildAt(0);
            m_title = (GTextField)GetChildAt(1);
            m_des = (GTextField)GetChildAt(2);
            m_btn_select = (GButton)GetChildAt(3);
            m_skillpreview = (UI_CardSkillDisplay)GetChildAt(4);
            m_show = GetTransitionAt(0);
            m_hide = GetTransitionAt(1);
        }
    }
}