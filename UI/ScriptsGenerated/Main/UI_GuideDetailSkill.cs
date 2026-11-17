/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideDetailSkill : GComponent
    {
        public UI_GuideSlot m_slot;
        public GList m_rankList;
        public GTextField m_Name;
        public UI_GuideRareBtoS m_rank;
        public const string URL = "ui://q68vr2bfn6szt3";

        public static UI_GuideDetailSkill CreateInstance()
        {
            return (UI_GuideDetailSkill)UIPackage.CreateObject("Main", "GuideDetailSkill");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_slot = (UI_GuideSlot)GetChildAt(2);
            m_rankList = (GList)GetChildAt(3);
            m_Name = (GTextField)GetChildAt(4);
            m_rank = (UI_GuideRareBtoS)GetChildAt(5);
        }
    }
}