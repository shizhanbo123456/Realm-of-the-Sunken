/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_DayNight : GComponent
    {
        public GImage m_pointer;
        public const string URL = "ui://q68vr2bfisq1r1";

        public static UI_DayNight CreateInstance()
        {
            return (UI_DayNight)UIPackage.CreateObject("Main", "DayNight");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_pointer = (GImage)GetChildAt(1);
        }
    }
}