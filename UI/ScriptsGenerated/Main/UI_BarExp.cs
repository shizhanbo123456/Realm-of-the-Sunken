/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BarExp : GComponent
    {
        public GImage m_fill;
        public const string URL = "ui://q68vr2bfisq1r3";

        public static UI_BarExp CreateInstance()
        {
            return (UI_BarExp)UIPackage.CreateObject("Main", "BarExp");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_fill = (GImage)GetChildAt(1);
        }
    }
}