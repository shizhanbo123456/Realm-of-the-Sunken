/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_CampInfoPanel : GComponent
    {
        public UI_CampInfo m_camp_attack;
        public UI_CampInfo m_camp_defense;
        public UI_CampInfo m_camp_spectate;
        public const string URL = "ui://q68vr2bflyekrg";

        public static UI_CampInfoPanel CreateInstance()
        {
            return (UI_CampInfoPanel)UIPackage.CreateObject("Main", "CampInfoPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_camp_attack = (UI_CampInfo)GetChildAt(0);
            m_camp_defense = (UI_CampInfo)GetChildAt(1);
            m_camp_spectate = (UI_CampInfo)GetChildAt(2);
        }
    }
}