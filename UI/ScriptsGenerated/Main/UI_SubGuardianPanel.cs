/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SubGuardianPanel : GComponent
    {
        public Controller m_killed;
        public UI_BarGuardianSub m_bar;
        public GTextField m_guardian_name;
        public GTextField m_health_percentage;
        public const string URL = "ui://q68vr2bfisq1rc";

        public static UI_SubGuardianPanel CreateInstance()
        {
            return (UI_SubGuardianPanel)UIPackage.CreateObject("Main", "SubGuardianPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_killed = GetControllerAt(0);
            m_bar = (UI_BarGuardianSub)GetChildAt(1);
            m_guardian_name = (GTextField)GetChildAt(3);
            m_health_percentage = (GTextField)GetChildAt(4);
        }
    }
}