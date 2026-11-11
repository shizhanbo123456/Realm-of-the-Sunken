/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_CharacterAttributePanel : GComponent
    {
        public Controller m_character_selcted;
        public GList m_attrList;
        public GList m_special_ability_list;
        public GButton m_btn_select_character;
        public const string URL = "ui://q68vr2bflwt1po";

        public static UI_CharacterAttributePanel CreateInstance()
        {
            return (UI_CharacterAttributePanel)UIPackage.CreateObject("Main", "CharacterAttributePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_character_selcted = GetControllerAt(0);
            m_attrList = (GList)GetChildAt(0);
            m_special_ability_list = (GList)GetChildAt(5);
            m_btn_select_character = (GButton)GetChildAt(6);
        }
    }
}