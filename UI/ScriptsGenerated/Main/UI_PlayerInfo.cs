/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_PlayerInfo : GComponent
    {
        public GTextField m_label_playerName;
        public const string URL = "ui://q68vr2bfphy0ii";

        public static UI_PlayerInfo CreateInstance()
        {
            return (UI_PlayerInfo)UIPackage.CreateObject("Main", "PlayerInfo");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_label_playerName = (GTextField)GetChildAt(0);
        }
    }
}