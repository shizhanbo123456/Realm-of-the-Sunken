/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideScoreTab : GComponent
    {
        public Controller m_highlight;
        public const string URL = "ui://q68vr2bfn6szsy";

        public static UI_GuideScoreTab CreateInstance()
        {
            return (UI_GuideScoreTab)UIPackage.CreateObject("Main", "GuideScoreTab");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_highlight = GetControllerAt(0);
        }
    }
}