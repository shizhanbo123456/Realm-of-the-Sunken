/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_LobbyPanel : GComponent
    {
        public GButton m_btn_prepare;
        public GButton m_btn_exit;
        public GTextField m_spectate_current_member_count;
        public UI_LobbCampOperation m_btn_joinDefense;
        public UI_LobbCampOperation m_btn_joinAttack;
        public GButton m_btn_settings;
        public const string URL = "ui://q68vr2bftwqeh8";

        public static UI_LobbyPanel CreateInstance()
        {
            return (UI_LobbyPanel)UIPackage.CreateObject("Main", "LobbyPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_btn_prepare = (GButton)GetChildAt(1);
            m_btn_exit = (GButton)GetChildAt(2);
            m_spectate_current_member_count = (GTextField)GetChildAt(4);
            m_btn_joinDefense = (UI_LobbCampOperation)GetChildAt(6);
            m_btn_joinAttack = (UI_LobbCampOperation)GetChildAt(7);
            m_btn_settings = (GButton)GetChildAt(8);
        }
    }
}