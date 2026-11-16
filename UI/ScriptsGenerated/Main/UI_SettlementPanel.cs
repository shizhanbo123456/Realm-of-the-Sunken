/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SettlementPanel : GComponent
    {
        public UI_BattleResult m_battle_result;
        public UI_BattleResultDetail m_battle_result_detail;
        public const string URL = "ui://q68vr2bfv4rxs2";

        public static UI_SettlementPanel CreateInstance()
        {
            return (UI_SettlementPanel)UIPackage.CreateObject("Main", "SettlementPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_battle_result = (UI_BattleResult)GetChildAt(0);
            m_battle_result_detail = (UI_BattleResultDetail)GetChildAt(1);
        }
    }
}