/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_CharacterSelectionPanel : GComponent
    {
        public Controller m_current_selected_camp;
        public GList m_characterList;
        public GButton m_tab_attacker;
        public GButton m_tab_defenser;
        public const string URL = "ui://q68vr2bfl6m9nz";

        public static UI_CharacterSelectionPanel CreateInstance()
        {
            return (UI_CharacterSelectionPanel)UIPackage.CreateObject("Main", "CharacterSelectionPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_current_selected_camp = GetControllerAt(0);
            m_characterList = (GList)GetChildAt(0);
            m_tab_attacker = (GButton)GetChildAt(1);
            m_tab_defenser = (GButton)GetChildAt(2);
        }
    }
}