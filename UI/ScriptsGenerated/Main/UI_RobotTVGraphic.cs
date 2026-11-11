/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_RobotTVGraphic : GComponent
    {
        public Controller m_c1;
        public const string URL = "ui://q68vr2bfr56bqb";

        public static UI_RobotTVGraphic CreateInstance()
        {
            return (UI_RobotTVGraphic)UIPackage.CreateObject("Main", "RobotTVGraphic");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_c1 = GetControllerAt(0);
        }
    }
}