/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideItemSkill : GButton
    {
        public Controller m_Level;
        public UI_GuideRareBtoS m_rare_BtoS;
        public GTextField m_des;
        public UI_GuideSlot m_mask;
        public const string URL = "ui://q68vr2bfn6szt0";

        public static UI_GuideItemSkill CreateInstance()
        {
            return (UI_GuideItemSkill)UIPackage.CreateObject("Main", "GuideItemSkill");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_Level = GetControllerAt(1);
            m_rare_BtoS = (UI_GuideRareBtoS)GetChildAt(3);
            m_des = (GTextField)GetChildAt(4);
            m_mask = (UI_GuideSlot)GetChildAt(5);
        }
    }
}