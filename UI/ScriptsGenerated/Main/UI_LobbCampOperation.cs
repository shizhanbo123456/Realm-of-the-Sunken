/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_LobbCampOperation : GComponent
    {
        public Controller m_quit_mode;
        public GButton m_join;
        public GButton m_quit;
        public GTextField m_label_AI_count;
        public GButton m_add_ai;
        public GButton m_remove_ai;
        public const string URL = "ui://q68vr2bflwt1px";

        public static UI_LobbCampOperation CreateInstance()
        {
            return (UI_LobbCampOperation)UIPackage.CreateObject("Main", "LobbCampOperation");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_quit_mode = GetControllerAt(0);
            m_join = (GButton)GetChildAt(0);
            m_quit = (GButton)GetChildAt(1);
            m_label_AI_count = (GTextField)GetChildAt(3);
            m_add_ai = (GButton)GetChildAt(4);
            m_remove_ai = (GButton)GetChildAt(5);
        }
    }
}