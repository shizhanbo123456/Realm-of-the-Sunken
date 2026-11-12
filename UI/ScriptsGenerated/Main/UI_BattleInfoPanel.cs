/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_BattleInfoPanel : GComponent
    {
        public UI_DayNight m_display_day_night;
        public GButton m_btn_settings;
        public GTextField m_time_left;
        public GButton m_btn_quick_message;
        public const string URL = "ui://q68vr2bflyekrf";

        public static UI_BattleInfoPanel CreateInstance()
        {
            return (UI_BattleInfoPanel)UIPackage.CreateObject("Main", "BattleInfoPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_display_day_night = (UI_DayNight)GetChildAt(0);
            m_btn_settings = (GButton)GetChildAt(1);
            m_time_left = (GTextField)GetChildAt(3);
            m_btn_quick_message = (GButton)GetChildAt(4);
        }
    }
}