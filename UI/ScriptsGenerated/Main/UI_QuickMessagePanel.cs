/** This is an automatically generated class by FairyGUI. Please do not modify it. **/

using FairyGUI;
using FairyGUI.Utils;

namespace Ros.UI.Main
{
    public partial class UI_QuickMessagePanel : GComponent
    {
        public UI_QuickMessageButton m_btn0;
        public UI_QuickMessageButton m_btn1;
        public UI_QuickMessageButton m_btn2;
        public UI_QuickMessageButton m_btn3;
        public UI_QuickMessageButton m_btn4;
        public UI_QuickMessageButton m_btn5;
        public UI_QuickMessageButton m_btn6;
        public UI_QuickMessageButton m_btn7;
        public Transition m_t1;
        public const string URL = "ui://q68vr2bfsmy0ro";

        public static UI_QuickMessagePanel CreateInstance()
        {
            return (UI_QuickMessagePanel)UIPackage.CreateObject("Main", "QuickMessagePanel");
        }

        public override void ConstructFromXML(XML xml)
        {
            base.ConstructFromXML(xml);

            m_btn0 = (UI_QuickMessageButton)GetChildAt(1);
            m_btn1 = (UI_QuickMessageButton)GetChildAt(2);
            m_btn2 = (UI_QuickMessageButton)GetChildAt(3);
            m_btn3 = (UI_QuickMessageButton)GetChildAt(4);
            m_btn4 = (UI_QuickMessageButton)GetChildAt(5);
            m_btn5 = (UI_QuickMessageButton)GetChildAt(6);
            m_btn6 = (UI_QuickMessageButton)GetChildAt(7);
            m_btn7 = (UI_QuickMessageButton)GetChildAt(8);
            m_t1 = GetTransitionAt(0);
        }
    }
}