/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_QuickMessagePointer : GComponent
    {
        public UI_QuickMessageGraphic m_graphic;
        public GComponent m_arrow;
        public const string URL = "ui://q68vr2bfq1vgrt";

        public static UI_QuickMessagePointer CreateInstance()
        {
            return (UI_QuickMessagePointer)UIPackage.CreateObject("Main", "QuickMessagePointer");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_graphic = (UI_QuickMessageGraphic)GetChildAt(0);
            m_arrow = (GComponent)GetChildAt(1);
        }
    }
}