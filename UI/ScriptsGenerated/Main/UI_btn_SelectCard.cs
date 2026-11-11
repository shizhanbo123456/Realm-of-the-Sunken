/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_btn_SelectCard : GComponent
    {
        public Controller m_level;
        public GTextField m_label_num;
        public const string URL = "ui://q68vr2bflwt1py";

        public static UI_btn_SelectCard CreateInstance()
        {
            return (UI_btn_SelectCard)UIPackage.CreateObject("Main", "btn_SelectCard");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_level = GetControllerAt(0);
            m_label_num = (GTextField)GetChildAt(6);
        }
    }
}