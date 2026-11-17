/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideSlot : GComponent
    {
        public GComponent m_mask;
        public const string URL = "ui://q68vr2bfn6szt1";

        public static UI_GuideSlot CreateInstance()
        {
            return (UI_GuideSlot)UIPackage.CreateObject("Main", "GuideSlot");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_mask = (GComponent)GetChildAt(1);
        }
    }
}