using FairyGUI;

using Ros.Config;
namespace Ros.UI.Main
{
    // 快捷指令悬浮提示：正文取自 quick_message.uiText
    public partial class UI_QuickMessageTip
    {
        public void Fill(int messageId)
        {
            m_title.text = QuickMessageConfig.Get(messageId).uiText;
        }

        public void ShowAtPointer() => TipLayout.PlaceAtPointer(this);

        public void Hide() => visible = false;
    }
}
