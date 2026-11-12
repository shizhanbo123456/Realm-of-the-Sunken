/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_QuickMessageButton : GButton
    {
        public UI_QuickMessageGraphic m_graphic_common;
        public UI_QuickMessageGraphic m_graphic_over;
        public UI_QuickMessageGraphic m_graphic_selected;
        public const string URL = "ui://q68vr2bfsmy0ri";

        public static UI_QuickMessageButton CreateInstance()
        {
            return (UI_QuickMessageButton)UIPackage.CreateObject("Main", "QuickMessageButton");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_graphic_common = (UI_QuickMessageGraphic)GetChildAt(0);
            m_graphic_over = (UI_QuickMessageGraphic)GetChildAt(1);
            m_graphic_selected = (UI_QuickMessageGraphic)GetChildAt(2);
        }
    }
}