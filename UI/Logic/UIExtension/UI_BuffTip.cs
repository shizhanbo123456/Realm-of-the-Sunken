using FairyGUI;

using Ros.Config;
namespace Ros.UI.Main
{
    // buff 悬浮提示：名称与描述取自 effect.csv
    public partial class UI_BuffTip
    {
        public void Fill(int buffType)
        {
            var cfg = EffectConfig.Get(buffType);
            m_buff_name.text = cfg.name;
            m_buff_des.text = cfg.description;
        }

        public void ShowAtPointer() => TipLayout.PlaceAtPointer(this);

        public void Hide() => visible = false;
    }
}
