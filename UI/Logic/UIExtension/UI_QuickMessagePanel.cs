using System;
using FairyGUI;

using Ros.Config;
namespace Ros.UI.Main
{
    // 快捷指令轮盘：8 个图标按钮按 0~7 号指令排布，点触发 m_t1 展开；按钮外观由 QuickMessageGraphic.m_type 档位区分
    public partial class UI_QuickMessagePanel
    {
        private Action<int> onSend;

        // 创建后调用一次：按钮与 quick_message.csv 的行一一对应，数量对不上直接抛
        public void Build(Action<int> onSend)
        {
            this.onSend = onSend;
            UI_QuickMessageButton[] all =
            {
                m_btn0, m_btn1, m_btn2, m_btn3, m_btn4, m_btn5, m_btn6, m_btn7,
            };
            if (all.Length != QuickMessageConfig.content.Count)
                throw new FormatException($"[快捷指令] 轮盘按钮 {all.Length} 个，quick_message.csv 有 {QuickMessageConfig.content.Count} 行");

            for (int i = 0; i < all.Length; i++)
            {
                int messageId = i;
                UI_QuickMessageButton button = all[i];
                button.m_graphic_common.m_type.selectedIndex = messageId;
                button.m_graphic_over.m_type.selectedIndex = messageId;
                button.m_graphic_selected.m_type.selectedIndex = messageId;
                button.onClick.Set(() => Click(messageId));
            }
            Close();
        }

        public bool IsOpen => visible;

        public void Open()
        {
            visible = true;
            touchable = true;
            m_t1.Play(); // 从圆心弹出
        }

        public void Close()
        {
            m_t1.Stop();
            visible = false;
            touchable = false; // 收起时不吃点击
        }

        private void Click(int messageId)
        {
            Close();
            onSend(messageId);
        }
    }
}
