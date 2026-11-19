/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SettingsPanel : GComponent
    {
        public GButton m_close2;
        public GButton m_close;
        public UI_panel_2 m_panel;
        public GList m_settings;
        public const string URL = "ui://q68vr2bfiq0omn";

        public static UI_SettingsPanel CreateInstance()
        {
            return (UI_SettingsPanel)UIPackage.CreateObject("Main", "SettingsPanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_close2 = (GButton)GetChildAt(0);
            m_close = (GButton)GetChildAt(1);
            m_panel = (UI_panel_2)GetChildAt(2);
            m_settings = (GList)GetChildAt(3);
        }
    }
}