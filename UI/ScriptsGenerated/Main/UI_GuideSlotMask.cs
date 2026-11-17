/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_GuideSlotMask : GComponent
    {
        public GLoader m_icon;
        public const string URL = "ui://q68vr2bfn6szt2";

        public static UI_GuideSlotMask CreateInstance()
        {
            return (UI_GuideSlotMask)UIPackage.CreateObject("Main", "GuideSlotMask");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_icon = (GLoader)GetChildAt(1);
        }
    }
}