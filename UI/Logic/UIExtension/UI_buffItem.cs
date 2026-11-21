using System.Collections.Generic;
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
            if (cfg.positive == true) m_negative.selectedIndex = 0; // 0 正面
            else m_negative.selectedIndex = 1;                      // 1 负面
            Texture icon = null;
            Dictionary<int, Texture> map = null;
            if (Tool.ResourceManager != null) map = Tool.ResourceManager.BuffIconMap;
            if (map != null && map.TryGetValue(cfg.iconId, out var tex)) icon = tex;
            if (icon != null) m_mask.m_loader.texture = new NTexture(icon); // 没配图标 → 不显示
            else m_mask.m_loader.texture = null;
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
