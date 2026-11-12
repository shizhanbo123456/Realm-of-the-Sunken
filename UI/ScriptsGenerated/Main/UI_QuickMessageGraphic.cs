/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_QuickMessageGraphic : GComponent
    {
        public Controller m_type;
        public const string URL = "ui://q68vr2bfsmy0rl";

        public static UI_QuickMessageGraphic CreateInstance()
        {
            return (UI_QuickMessageGraphic)UIPackage.CreateObject("Main", "QuickMessageGraphic");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_type = GetControllerAt(0);
        }
    }
}