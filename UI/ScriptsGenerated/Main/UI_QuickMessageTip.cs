/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_QuickMessageTip : GComponent
    {
        public GTextField m_title;
        public const string URL = "ui://q68vr2bfnhkxta";

        public static UI_QuickMessageTip CreateInstance()
        {
            return (UI_QuickMessageTip)UIPackage.CreateObject("Main", "QuickMessageTip");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_title = (GTextField)GetChildAt(1);
        }
    }
}