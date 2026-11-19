/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_RuleContent : GComponent
    {
        public GTextField m_cotent;
        public const string URL = "ui://q68vr2bfmnktt9";

        public static UI_RuleContent CreateInstance()
        {
            return (UI_RuleContent)UIPackage.CreateObject("Main", "RuleContent");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_cotent = (GTextField)GetChildAt(0);
        }
    }
}