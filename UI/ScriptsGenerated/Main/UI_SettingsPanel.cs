/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SettingsPanel : GComponent
    {
        public UI_panel_2 m_panel;
        public UI_SettingScrollView m_settings_scroll_view;
        public const string URL = "ui://q68vr2bfiq0omn";

        public static UI_SettingsPanel CreateInstance()
        {
            return (UI_SettingsPanel)UIPackage.CreateObject("Main", "SettingsPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_panel = (UI_panel_2)GetChildAt(1);
            m_settings_scroll_view = (UI_SettingScrollView)GetChildAt(2);
        }
    }
}