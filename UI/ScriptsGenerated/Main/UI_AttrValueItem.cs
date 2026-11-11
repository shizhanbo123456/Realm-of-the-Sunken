/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_AttrValueItem : GComponent
    {
        public Controller m_filled;
        public const string URL = "ui://q68vr2bflwt1pm";

        public static UI_AttrValueItem CreateInstance()
        {
            return (UI_AttrValueItem)UIPackage.CreateObject("Main", "AttrValueItem");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_filled = GetControllerAt(0);
        }
    }
}