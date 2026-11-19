/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_PanelBackground : GComponent
    {
        public GGraph m_black_mask;
        public GButton m_full_screen_close;
        public const string URL = "ui://q68vr2bfct9pt8";

        public static UI_PanelBackground CreateInstance()
        {
            return (UI_PanelBackground)UIPackage.CreateObject("Main", "PanelBackground");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_black_mask = (GGraph)GetChildAt(0);
            m_full_screen_close = (GButton)GetChildAt(1);
        }
    }
}