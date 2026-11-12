/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_ChatItem : GComponent
    {
        public GTextField m_content;
        public const string URL = "ui://q68vr2bfobkwj5";

        public static UI_ChatItem CreateInstance()
        {
            return (UI_ChatItem)UIPackage.CreateObject("Main", "ChatItem");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_content = (GTextField)GetChildAt(0);
        }
    }
}