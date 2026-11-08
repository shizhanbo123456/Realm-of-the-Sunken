/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_RemanePanel : GComponent
    {
        public UI_panel_2 m_panel;
        public GTextInput m_input;
        public GButton m_confirm;
        public const string URL = "ui://q68vr2bfpkovnp";

        public static UI_RemanePanel CreateInstance()
        {
            return (UI_RemanePanel)UIPackage.CreateObject("Main", "RemanePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_panel = (UI_panel_2)GetChildAt(1);
            m_input = (GTextInput)GetChildAt(2);
            m_confirm = (GButton)GetChildAt(4);
        }
    }
}