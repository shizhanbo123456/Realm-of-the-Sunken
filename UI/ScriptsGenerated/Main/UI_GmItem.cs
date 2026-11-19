/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GmItem : GComponent
    {
        public Controller m_content;
        public Controller m_type;
        public GTextField m_label_main_column;
        public GTextField m_label_sub_column;
        public GTextInput m_input;
        public GButton m_btn;
        public const string URL = "ui://q68vr2bftjktiq";

        public static UI_GmItem CreateInstance()
        {
            return (UI_GmItem)UIPackage.CreateObject("Main", "GmItem");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_content = GetControllerAt(0);
            m_type = GetControllerAt(1);
            m_label_main_column = (GTextField)GetChildAt(1);
            m_label_sub_column = (GTextField)GetChildAt(3);
            m_input = (GTextInput)GetChildAt(4);
            m_btn = (GButton)GetChildAt(8);
        }
    }
}