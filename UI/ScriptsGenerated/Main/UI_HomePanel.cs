/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_HomePanel : GComponent
    {
        public UI_HomeConnect m_connectPanel;
        public UI_PlayerInfo m_playerInfo;
        public UI_panel_2 m_panel;
        public GList m_characterList;
        public GButton m_btn_defense;
        public GButton m_btn_attack;
        public GList m_attrList;
        public GImage m_bar_fill_eexp;
        public GTextField m_label_exp;
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
            m_panel = (UI_panel_2)GetChildAt(3);
            m_characterList = (GList)GetChildAt(4);
            m_btn_defense = (GButton)GetChildAt(5);
            m_btn_attack = (GButton)GetChildAt(6);
            m_attrList = (GList)GetChildAt(9);
            m_bar_fill_eexp = (GImage)GetChildAt(11);
            m_label_exp = (GTextField)GetChildAt(12);
        }
    }
}