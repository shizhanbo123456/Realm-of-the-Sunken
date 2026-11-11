/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_CardSkillDisplay : GComponent
    {
        public GLoader m_loader_icon;
        public const string URL = "ui://q68vr2bfr56bqd";

        public static UI_CardSkillDisplay CreateInstance()
        {
            return (UI_CardSkillDisplay)UIPackage.CreateObject("Main", "CardSkillDisplay");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_loader_icon = (GLoader)GetChildAt(2);
        }
    }
}