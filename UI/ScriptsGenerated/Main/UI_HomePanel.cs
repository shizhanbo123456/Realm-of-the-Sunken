/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_HomePanel : GComponent
    {
        public Controller m_show_character_panel;
        public GButton m_btn_select_character;
        public UI_HomeConnect m_connectPanel;
        public UI_PlayerInfo m_playerInfo;
        public UI_HomeCharacterList m_characterList;
        public const string URL = "ui://q68vr2bfvjeqgv";

        public static UI_HomePanel CreateInstance()
        {
            return (UI_HomePanel)UIPackage.CreateObject("Main", "HomePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_show_character_panel = GetControllerAt(0);
            m_btn_select_character = (GButton)GetChildAt(0);
            m_connectPanel = (UI_HomeConnect)GetChildAt(1);
            m_playerInfo = (UI_PlayerInfo)GetChildAt(2);
            m_characterList = (UI_HomeCharacterList)GetChildAt(3);
        }
    }
}