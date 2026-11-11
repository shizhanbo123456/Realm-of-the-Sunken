/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_HomePanel : GComponent
    {
        public UI_HomeConnect m_connectPanel;
        public UI_PlayerInfo m_playerInfo;
        public GTextField m_label_character_name;
        public UI_CharacterSelectionPanel m_character_selection_panel;
        public UI_CharacterAttributePanel m_character_attr_panel;
        public GButton m_btn_open_guide;
        public const string URL = "ui://q68vr2bfvjeqgv";

        public static UI_HomePanel CreateInstance()
        {
            return (UI_HomePanel)UIPackage.CreateObject("Main", "HomePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_connectPanel = (UI_HomeConnect)GetChildAt(0);
            m_playerInfo = (UI_PlayerInfo)GetChildAt(1);
            m_label_character_name = (GTextField)GetChildAt(2);
            m_character_selection_panel = (UI_CharacterSelectionPanel)GetChildAt(3);
            m_character_attr_panel = (UI_CharacterAttributePanel)GetChildAt(4);
            m_btn_open_guide = (GButton)GetChildAt(5);
        }
    }
}