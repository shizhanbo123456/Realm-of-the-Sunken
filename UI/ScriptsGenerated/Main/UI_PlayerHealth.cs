/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_PlayerHealth : GComponent
    {
        public GTextField m_label_current_label;
        public GImage m_bar_fill_health;
        public GTextField m_label_health;
        public GImage m_bar_fill_exp;
        public GTextField m_label_exp;
        public UI_buffList m_buffList;
        public const string URL = "ui://q68vr2bfjtpbhg";

        public static UI_PlayerHealth CreateInstance()
        {
            return (UI_PlayerHealth)UIPackage.CreateObject("Main", "PlayerHealth");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_label_current_label = (GTextField)GetChildAt(0);
            m_bar_fill_health = (GImage)GetChildAt(2);
            m_label_health = (GTextField)GetChildAt(3);
            m_bar_fill_exp = (GImage)GetChildAt(5);
            m_label_exp = (GTextField)GetChildAt(6);
            m_buffList = (UI_buffList)GetChildAt(7);
        }
    }
}