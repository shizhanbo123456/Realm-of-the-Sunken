/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_RulePanel : GComponent
    {
        public UI_panel_2 m_panel;
        public GList m_rule_buttons;
        public GList m_contentList;
        public const string URL = "ui://q68vr2bfhas4t7";

        public static UI_RulePanel CreateInstance()
        {
            return (UI_RulePanel)UIPackage.CreateObject("Main", "RulePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_panel = (UI_panel_2)GetChildAt(0);
            m_rule_buttons = (GList)GetChildAt(2);
            m_contentList = (GList)GetChildAt(3);
        }
    }
}