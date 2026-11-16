using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 技能槽条目：图标、冷却填充、库存、空槽、高亮；悬浮提示自发弹出
    public partial class UI_SkillListItem
    {
        private bool tipHooked;
        private int tipSkillId;

        public void Fill(in UI_SkillList.Slot data, bool selected = false)
        {
            bool empty = data.skillId < 0;
            // 鼠标操作下没有键位标签
            if (m_key != null)
            {
                m_key.text = "";
                m_key.visible = false;
            }
            if (m_empty != null) m_empty.selectedIndex = empty ? 1 : 0;
            if (m_highlight != null) m_highlight.selectedIndex = selected ? 1 : 0;

            Texture icon = empty ? null : UI_SkillList.ResolveIcon(data.skillId);
            if (m_loader_icon != null)
            {
                m_loader_icon.texture = icon != null ? new NTexture(icon) : null;
                m_loader_icon.visible = icon != null;
                m_loader_icon.fillAmount = data.unlimited || empty ? 1f : data.recharge01; // 无限库存/空槽进度恒满
            }
            // 底图与图标同一张图，前后层级由 FGUI 保证
            if (m_loader_iconBase != null)
            {
                m_loader_iconBase.texture = icon != null ? new NTexture(icon) : null;
                m_loader_iconBase.visible = icon != null;
            }
            if (m_storeList != null) m_storeList.numItems = data.unlimited || empty ? 0 : data.store;

            tipSkillId = data.skillId;
            if (tipHooked) return; // 列表复用条目，事件只挂一次，回调里读当前技能
            tipHooked = true;
            onRollOver.Set(() => { if (tipSkillId >= 0) TipHost.ShowSkill(tipSkillId, this); }); // 空槽不弹提示
            onRollOut.Set(TipHost.Hide);
        }
    }
}
