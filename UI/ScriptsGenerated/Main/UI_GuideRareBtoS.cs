/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideRareBtoS : GComponent
    {
        public Controller m_level;
        public const string URL = "ui://q68vr2bfn6szsw";

        public static UI_GuideRareBtoS CreateInstance()
        {
            return (UI_GuideRareBtoS)UIPackage.CreateObject("Main", "GuideRareBtoS");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_level = GetControllerAt(0);
        }
    }
}