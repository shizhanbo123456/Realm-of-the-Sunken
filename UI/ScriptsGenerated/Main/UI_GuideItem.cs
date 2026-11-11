/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideItem : GComponent
    {
        public GLoader m_icon;
        public GTextField m_title;
        public GTextField m_des;
        public const string URL = "ui://q68vr2bflwt1pv";

        public static UI_GuideItem CreateInstance()
        {
            return (UI_GuideItem)UIPackage.CreateObject("Main", "GuideItem");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_icon = (GLoader)GetChildAt(1);
            m_title = (GTextField)GetChildAt(2);
            m_des = (GTextField)GetChildAt(3);
        }
    }
}