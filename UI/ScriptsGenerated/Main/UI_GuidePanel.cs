/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuidePanel : GComponent
    {
        public Controller m_buffTab;
        public GButton m_btn_close;
        public GList m_menu_btn_list;
        public GList m_item_list;
        public UI_GuideDetailSkill m_skill_detail;
        public const string URL = "ui://q68vr2bflwt1pt";

        public static UI_GuidePanel CreateInstance()
        {
            return (UI_GuidePanel)UIPackage.CreateObject("Main", "GuidePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_buffTab = GetControllerAt(0);
            m_btn_close = (GButton)GetChildAt(2);
            m_menu_btn_list = (GList)GetChildAt(3);
            m_item_list = (GList)GetChildAt(4);
            m_skill_detail = (UI_GuideDetailSkill)GetChildAt(5);
        }
    }
}