/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideMenuLabel : GComponent
    {
        public GTextField m_title;
        public const string URL = "ui://q68vr2bflwt1pu";

        public static UI_GuideMenuLabel CreateInstance()
        {
            return (UI_GuideMenuLabel)UIPackage.CreateObject("Main", "GuideMenuLabel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_title = (GTextField)GetChildAt(0);
        }
    }
}