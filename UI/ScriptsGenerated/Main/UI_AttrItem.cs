/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_AttrItem : GComponent
    {
        public GImage m_next_level_fill;
        public GImage m_growth_fill;
        public GImage m_base;
        public GTextField m_attrName;
        public GTextField m_attrValue;
        public const string URL = "ui://q68vr2bftwqeh5";

        public static UI_AttrItem CreateInstance()
        {
            return (UI_AttrItem)UIPackage.CreateObject("Main", "AttrItem");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_next_level_fill = (GImage)GetChildAt(0);
            m_growth_fill = (GImage)GetChildAt(1);
            m_base = (GImage)GetChildAt(2);
            m_attrName = (GTextField)GetChildAt(4);
            m_attrValue = (GTextField)GetChildAt(5);
        }
    }
}