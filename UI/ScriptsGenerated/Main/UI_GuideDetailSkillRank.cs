/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideDetailSkillRank : GComponent
    {
        public GTextField m_rank_name;
        public GList m_rank_tabs;
        public UI_GuideRareDtoS m_rank;
        public const string URL = "ui://q68vr2bfn6szt4";

        public static UI_GuideDetailSkillRank CreateInstance()
        {
            return (UI_GuideDetailSkillRank)UIPackage.CreateObject("Main", "GuideDetailSkillRank");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_rank_name = (GTextField)GetChildAt(0);
            m_rank_tabs = (GList)GetChildAt(1);
            m_rank = (UI_GuideRareDtoS)GetChildAt(2);
        }
    }
}