/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_CampInfo : GComponent
    {
        public Controller m_c1;
        public Controller m_is_in_tis_camp;
        public GTextField m_member_count;
        public const string URL = "ui://q68vr2bflyekrh";

        public static UI_CampInfo CreateInstance()
        {
            return (UI_CampInfo)UIPackage.CreateObject("Main", "CampInfo");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_c1 = GetControllerAt(0);
            m_is_in_tis_camp = GetControllerAt(1);
            m_member_count = (GTextField)GetChildAt(2);
        }
    }
}