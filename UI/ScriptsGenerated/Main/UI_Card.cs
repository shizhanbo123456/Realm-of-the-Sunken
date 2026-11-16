/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_Card : GComponent
    {
        public UI_CardGraphic m_cardGraphic;
        public Transition m_show;
        public Transition m_hide;
        public const string URL = "ui://q68vr2bflwt1q2";

        public static UI_Card CreateInstance()
        {
            return (UI_Card)UIPackage.CreateObject("Main", "Card");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_cardGraphic = (UI_CardGraphic)GetChildAt(0);
            m_show = GetTransitionAt(0);
            m_hide = GetTransitionAt(1);
        }
    }
}