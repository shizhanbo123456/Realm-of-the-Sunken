/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BarEntity : GComponent
    {
        public GImage m_fill;
        public const string URL = "ui://q68vr2bfsmy0rp";

        public static UI_BarEntity CreateInstance()
        {
            return (UI_BarEntity)UIPackage.CreateObject("Main", "BarEntity");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_fill = (GImage)GetChildAt(1);
        }
    }
}