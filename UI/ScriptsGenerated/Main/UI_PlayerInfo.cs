/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_PlayerInfo : GComponent
    {
        public GImage m_bar_fill_energy;
        public GTextField m_label_exp;
        public GTextField m_label_playerName;
        public GButton m_remame_button;
        public const string URL = "ui://q68vr2bfphy0ii";

        public static UI_PlayerInfo CreateInstance()
        {
            return (UI_PlayerInfo)UIPackage.CreateObject("Main", "PlayerInfo");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_bar_fill_energy = (GImage)GetChildAt(2);
            m_label_exp = (GTextField)GetChildAt(3);
            m_label_playerName = (GTextField)GetChildAt(7);
            m_remame_button = (GButton)GetChildAt(8);
        }
    }
}