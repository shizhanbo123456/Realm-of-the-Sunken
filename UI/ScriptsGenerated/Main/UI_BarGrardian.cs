/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BarGrardian : GComponent
    {
        public GImage m_fill;
        public const string URL = "ui://q68vr2bfisq1ra";

        public static UI_BarGrardian CreateInstance()
        {
            return (UI_BarGrardian)UIPackage.CreateObject("Main", "BarGrardian");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_fill = (GImage)GetChildAt(1);
        }
    }
}