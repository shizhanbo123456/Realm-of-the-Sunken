/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_EntityBar : GComponent
    {
        public Controller m_is_enemy;
        public UI_buffList m_buff_list;
        public GTextField m_entity_name;
        public UI_BarEntity m_bar;
        public UI_PlayerCampIcon m_campIcon;
        public GTextField m_label_health;
        public const string URL = "ui://q68vr2bfjtpbia";

        public static UI_EntityBar CreateInstance()
        {
            return (UI_EntityBar)UIPackage.CreateObject("Main", "EntityBar");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_is_enemy = GetControllerAt(0);
            m_buff_list = (UI_buffList)GetChildAt(0);
            m_entity_name = (GTextField)GetChildAt(1);
            m_bar = (UI_BarEntity)GetChildAt(2);
            m_campIcon = (UI_PlayerCampIcon)GetChildAt(3);
            m_label_health = (GTextField)GetChildAt(4);
        }
    }
}