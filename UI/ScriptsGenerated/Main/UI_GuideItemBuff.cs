/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideItemBuff : GButton
    {
        public GImage m_slot;
        public GTextField m_des;
        public const string URL = "ui://q68vr2bfn6szt6";

        public static UI_GuideItemBuff CreateInstance()
        {
            return (UI_GuideItemBuff)UIPackage.CreateObject("Main", "GuideItemBuff");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_slot = (GImage)GetChildAt(3);
            m_des = (GTextField)GetChildAt(4);
        }
    }
}