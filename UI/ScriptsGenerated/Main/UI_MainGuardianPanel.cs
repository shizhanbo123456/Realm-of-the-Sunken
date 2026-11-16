/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_MainGuardianPanel : GComponent
    {
        public UI_BarGrardian m_bar;
        public GTextField m_health_label;
        public GTextField m_protection_layer_count;
        public GTextField m_Name;
        public GTextField m_health_percentage;
        public const string URL = "ui://q68vr2bfisq1rd";

        public static UI_MainGuardianPanel CreateInstance()
        {
            return (UI_MainGuardianPanel)UIPackage.CreateObject("Main", "MainGuardianPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_bar = (UI_BarGrardian)GetChildAt(0);
            m_health_label = (GTextField)GetChildAt(2);
            m_protection_layer_count = (GTextField)GetChildAt(4);
            m_Name = (GTextField)GetChildAt(5);
            m_health_percentage = (GTextField)GetChildAt(7);
        }
    }
}