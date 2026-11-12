/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BarHealth : GComponent
    {
        public GImage m_fill;
        public const string URL = "ui://q68vr2bfisq1r2";

        public static UI_BarHealth CreateInstance()
        {
            return (UI_BarHealth)UIPackage.CreateObject("Main", "BarHealth");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_fill = (GImage)GetChildAt(1);
        }
    }
}