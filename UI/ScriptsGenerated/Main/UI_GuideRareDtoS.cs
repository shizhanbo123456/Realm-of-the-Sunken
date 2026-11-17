/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideRareDtoS : GComponent
    {
        public Controller m_level;
        public const string URL = "ui://q68vr2bflwt1pw";

        public static UI_GuideRareDtoS CreateInstance()
        {
            return (UI_GuideRareDtoS)UIPackage.CreateObject("Main", "GuideRareDtoS");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_level = GetControllerAt(0);
        }
    }
}