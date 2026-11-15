/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_SettingsScrollViewMask : GComponent
    {
        public UI_SettingScrollView m_settingsPanel;
        public const string URL = "ui://q68vr2bff08ks2";

        public static UI_SettingsScrollViewMask CreateInstance()
        {
            return (UI_SettingsScrollViewMask)UIPackage.CreateObject("Main", "SettingsScrollViewMask");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_settingsPanel = (UI_SettingScrollView)GetChildAt(1);
        }
    }
}