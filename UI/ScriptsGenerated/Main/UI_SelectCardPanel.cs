/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SelectCardPanel : GComponent
    {
        public GList m_list;
        public GButton m_btn_close;
        public GButton m_discard;
        public GTextField m_discard_description;
        public GTextField m_current_luck;
        public const string URL = "ui://q68vr2bflwt1pz";

        public static UI_SelectCardPanel CreateInstance()
        {
            return (UI_SelectCardPanel)UIPackage.CreateObject("Main", "SelectCardPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_list = (GList)GetChildAt(0);
            m_btn_close = (GButton)GetChildAt(1);
            m_discard = (GButton)GetChildAt(2);
            m_discard_description = (GTextField)GetChildAt(3);
            m_current_luck = (GTextField)GetChildAt(5);
        }
    }
}