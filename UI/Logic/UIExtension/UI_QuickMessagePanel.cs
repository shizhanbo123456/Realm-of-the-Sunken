using System;
using System.Collections.Generic;
using FairyGUI;

namespace Ros.UI.Main
{
    // 快捷指令轮盘：8 个图标按钮按 0~7 号指令排布，点触发 m_t1 展开；按钮外观由 QuickMessageGraphic.m_type 档位区分
    public partial class UI_QuickMessagePanel
    {
        private readonly List<UI_QuickMessageButton> buttons = new();
        private Action<int> onSend;

        // 创建后调用一次：按子件顺序绑定 0~7 号指令（底板是无名的 graph，不参与绑定）
        public void Build(Action<int> onSend)
        {
            this.onSend = onSend;
            buttons.Clear();
            for (int i = 0; i < numChildren; i++)
            {
                if (GetChildAt(i) is not UI_QuickMessageButton button) continue;
                int messageId = buttons.Count;
                if (messageId >= ConfigManager.QuickMessageConfig.content.Count)
                    throw new FormatException($"[快捷指令] 轮盘按钮数超过 quick_message.csv 的行数 {ConfigManager.QuickMessageConfig.content.Count}");
                ConfigManager.QuickMessageConfig.Get(messageId); // 缺行直接抛，不做兜底

                button.m_graphic_common.m_type.selectedIndex = messageId;
                button.m_graphic_over.m_type.selectedIndex = messageId;
                button.m_graphic_selected.m_type.selectedIndex = messageId;
                button.onClick.Set(() => Click(messageId));
                buttons.Add(button);
            }
            if (buttons.Count != ConfigManager.QuickMessageConfig.content.Count)
                throw new FormatException($"[快捷指令] 轮盘按钮 {buttons.Count} 个，quick_message.csv 有 {ConfigManager.QuickMessageConfig.content.Count} 行");
            Close();
        }

        public bool IsOpen => visible;

        public void Open()
        {
            visible = true;
            touchable = true;
            if (m_t1 != null) m_t1.Play(); // 从圆心弹出
        }

        public void Close()
        {
            if (m_t1 != null) m_t1.Stop();
            visible = false;
            touchable = false; // 收起时不吃点击
        }

        private void Click(int messageId)
        {
            Close();
            onSend?.Invoke(messageId);
        }
    }
}
