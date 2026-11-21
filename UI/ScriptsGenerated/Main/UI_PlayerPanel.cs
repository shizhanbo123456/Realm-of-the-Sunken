/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_PlayerPanel : GComponent
    {
        public UI_SkillList m_skill_list;
        public UI_BarExp m_bar_exp;
        public UI_BarHealth m_bar_health;
        public UI_buffList m_buffList;
        public UI_avaterMask m_avater_icon_mask;
        public GTextField m_label_level;
        public GTextField m_label_exp;
        public GTextField m_label_health;
        public const string URL = "ui://q68vr2bflyekre";

        public static UI_PlayerPanel CreateInstance()
        {
            return (UI_PlayerPanel)UIPackage.CreateObject("Main", "PlayerPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_skill_list = (UI_SkillList)GetChildAt(0);
            m_bar_exp = (UI_BarExp)GetChildAt(1);
            m_bar_health = (UI_BarHealth)GetChildAt(2);
            m_buffList = (UI_buffList)GetChildAt(3);
            m_avater_icon_mask = (UI_avaterMask)GetChildAt(4);
            m_label_level = (GTextField)GetChildAt(7);
            m_label_exp = (GTextField)GetChildAt(9);
            m_label_health = (GTextField)GetChildAt(10);
        }
    }
}