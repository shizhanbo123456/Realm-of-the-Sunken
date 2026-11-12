/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BattleBarPanel : GComponent
    {
        public UI_SubGuardianPanel m_sub1;
        public UI_MainGuardianPanel m_main;
        public UI_SubGuardianPanel m_sub2;
        public UI_SubGuardianPanel m_sub3;
        public const string URL = "ui://q68vr2bfiq0omj";

        public static UI_BattleBarPanel CreateInstance()
        {
            return (UI_BattleBarPanel)UIPackage.CreateObject("Main", "BattleBarPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_sub1 = (UI_SubGuardianPanel)GetChildAt(0);
            m_main = (UI_MainGuardianPanel)GetChildAt(1);
            m_sub2 = (UI_SubGuardianPanel)GetChildAt(2);
            m_sub3 = (UI_SubGuardianPanel)GetChildAt(3);
        }
    }
}