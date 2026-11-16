/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BattlePanel : GComponent
    {
        public Controller m_showRegenerationBar;
        public Controller m_onlook;
        public UI_empty_container m_world_element_container;
        public UI_Minimap m_Minimap;
        public UI_EventBanner m_eventBanner;
        public GImage m_regeneration_progressbar;
        public GComboBox m_onlookComboBox;
        public GList m_list_selectCard;
        public UI_BattleBarPanel m_barPanel;
        public UI_PlayerPanel m_playerPanel;
        public UI_CampInfoPanel m_CampInfoPanel;
        public UI_BattleInfoPanel m_BattleInfoPanel;
        public UI_ImportantEventBanner m_important_event_banner;
        public UI_SkillSelectPanel m_skillSelectPanel;
        public UI_GodViewMinimapPanel m_godViewPanel;
        public UI_LargeMinimapPanel m_largeMinimapPanel;
        public UI_SelectCardPanel m_selectCardPanel;
        public Transition m_showBanner;
        public Transition m_showHit;
        public const string URL = "ui://q68vr2bfjtpbhd";

        public static UI_BattlePanel CreateInstance()
        {
            return (UI_BattlePanel)UIPackage.CreateObject("Main", "BattlePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_showRegenerationBar = GetControllerAt(0);
            m_onlook = GetControllerAt(1);
            m_world_element_container = (UI_empty_container)GetChildAt(1);
            m_Minimap = (UI_Minimap)GetChildAt(2);
            m_eventBanner = (UI_EventBanner)GetChildAt(3);
            m_regeneration_progressbar = (GImage)GetChildAt(5);
            m_onlookComboBox = (GComboBox)GetChildAt(9);
            m_list_selectCard = (GList)GetChildAt(11);
            m_barPanel = (UI_BattleBarPanel)GetChildAt(12);
            m_playerPanel = (UI_PlayerPanel)GetChildAt(13);
            m_CampInfoPanel = (UI_CampInfoPanel)GetChildAt(14);
            m_BattleInfoPanel = (UI_BattleInfoPanel)GetChildAt(15);
            m_important_event_banner = (UI_ImportantEventBanner)GetChildAt(16);
            m_skillSelectPanel = (UI_SkillSelectPanel)GetChildAt(17);
            m_godViewPanel = (UI_GodViewMinimapPanel)GetChildAt(18);
            m_largeMinimapPanel = (UI_LargeMinimapPanel)GetChildAt(19);
            m_selectCardPanel = (UI_SelectCardPanel)GetChildAt(20);
            m_showBanner = GetTransitionAt(0);
            m_showHit = GetTransitionAt(1);
        }
    }
}