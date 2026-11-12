/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_QuickMessagePanel : GComponent
    {
        public Transition m_t1;
        public const string URL = "ui://q68vr2bfsmy0ro";

        public static UI_QuickMessagePanel CreateInstance()
        {
            return (UI_QuickMessagePanel)UIPackage.CreateObject("Main", "QuickMessagePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_t1 = GetTransitionAt(0);
        }
    }
}