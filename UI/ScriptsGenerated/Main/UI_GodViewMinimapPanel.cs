/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GodViewMinimapPanel : GComponent
    {
        public Controller m_show_character_detail;
        public UI_panel_2 m_panel;
        public UI_GodViewMinimapContainer m_largeMinimapContainer;
        public UI_RoleHead m_roleHead;
        public UI_BarHealth m_bar_health;
        public GTextField m_label_health;
        public UI_buffList m_buff_list;
        public UI_PlayerCampIcon m_campIcon;
        public GTextField m_entity_name;
        public GList m_activeSkillList;
        public GList m_skillList;
        public GList m_scaleButtonList;
        public const string URL = "ui://q68vr2bflp1tnh";

        public static UI_GodViewMinimapPanel CreateInstance()
        {
            return (UI_GodViewMinimapPanel)UIPackage.CreateObject("Main", "GodViewMinimapPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_show_character_detail = GetControllerAt(0);
            m_panel = (UI_panel_2)GetChildAt(0);
            m_largeMinimapContainer = (UI_GodViewMinimapContainer)GetChildAt(1);
            m_roleHead = (UI_RoleHead)GetChildAt(2);
            m_bar_health = (UI_BarHealth)GetChildAt(3);
            m_label_health = (GTextField)GetChildAt(4);
            m_buff_list = (UI_buffList)GetChildAt(5);
            m_campIcon = (UI_PlayerCampIcon)GetChildAt(6);
            m_entity_name = (GTextField)GetChildAt(7);
            m_activeSkillList = (GList)GetChildAt(8);
            m_skillList = (GList)GetChildAt(9);
            m_scaleButtonList = (GList)GetChildAt(11);
        }
    }
}