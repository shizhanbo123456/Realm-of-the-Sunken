using FairyGUI;
using UnityEngine;

using Ros.Config;
namespace Ros.UI.Main
{
    // buff 图标条目：正负档、图标、层数；悬浮提示自发弹出
    public partial class UI_buffItem
    {
        private bool tipHooked;
        private int tipType;

        public void Fill(int buffType, int level)
        {
            var cfg = EffectConfig.Get(buffType);
            m_negative.selectedIndex = cfg.positive == true ? 0 : 1; // 0 正面 / 1 负面
            Texture icon = null;
            var map = Tool.ResourceManager != null ? Tool.ResourceManager.BuffIconMap : null;
            if (map != null && map.TryGetValue(cfg.iconId, out var tex)) icon = tex;
            m_mask.m_loader.texture = icon != null ? new NTexture(icon) : null; // 没配图标 → 不显示
            // level 即显示层数：逐层计时类 = 各层等级之和，单层类 = 该条等级（服务端已聚合）
            m_layer_count.text = level.ToString();

            tipType = buffType;
            if (tipHooked) return; // 列表复用条目，事件只挂一次，回调里读当前类型
            tipHooked = true;
            onRollOver.Set(() => TipHost.ShowBuff(tipType, this));
            onRollOut.Set(TipHost.Hide);
        }
    }
}
