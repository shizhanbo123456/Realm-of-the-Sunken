/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_RobotTV : GButton
    {
        public UI_RobotTVGraphic m_graphic;
        public GTextField m_label;
        public Transition m_t0;
        public const string URL = "ui://q68vr2bfleavqe";

        public static UI_RobotTV CreateInstance()
        {
            return (UI_RobotTV)UIPackage.CreateObject("Main", "RobotTV");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_graphic = (UI_RobotTVGraphic)GetChildAt(0);
            m_label = (GTextField)GetChildAt(1);
            m_t0 = GetTransitionAt(0);
        }
    }
}