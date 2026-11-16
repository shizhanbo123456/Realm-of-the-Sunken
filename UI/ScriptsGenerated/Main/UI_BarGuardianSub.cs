/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BarGuardianSub : GComponent
    {
        public GImage m_fill;
        public const string URL = "ui://q68vr2bfisq1rb";

        public static UI_BarGuardianSub CreateInstance()
        {
            return (UI_BarGuardianSub)UIPackage.CreateObject("Main", "BarGuardianSub");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_fill = (GImage)GetChildAt(1);
        }
    }
}