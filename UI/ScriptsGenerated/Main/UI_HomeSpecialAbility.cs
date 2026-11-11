/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_HomeSpecialAbility : GComponent
    {
        public GTextField m_label;
        public const string URL = "ui://q68vr2bflwt1pp";

        public static UI_HomeSpecialAbility CreateInstance()
        {
            return (UI_HomeSpecialAbility)UIPackage.CreateObject("Main", "HomeSpecialAbility");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_label = (GTextField)GetChildAt(1);
        }
    }
}