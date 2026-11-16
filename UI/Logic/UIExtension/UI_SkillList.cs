using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 技能栏容器：条目数、数据快照与选中态，绘制与悬浮提示归 UI_SkillListItem
    public partial class UI_SkillList
    {
        // 单个槽位的表现数据，skillId < 0 = 空槽
        public readonly struct Slot
        {
            public readonly int skillId;
            public readonly float recharge01; // 本轮恢复进度，1 = 满
            public readonly int store;
            public readonly bool unlimited;   // 无限库存：不显示次数、进度恒满

            public Slot(int skillId, float recharge01, int store, bool unlimited)
            {
                this.skillId = skillId;
                this.recharge01 = recharge01;
                this.store = store;
                this.unlimited = unlimited;
            }
        }

        private readonly List<Slot> slots = new();
        private int selectedSlot; // 滚轮选中的槽位，用高光标出

        public bool IsBuilt { get; private set; }

        public int SlotCount { get; private set; }

        // 搭骨架：条目数 = 角色技能槽位数；悬浮提示由条目自身处理
        public void Build(int count)
        {
            var list = m_content;
            if (list == null) return;
            SlotCount = count;
            list.itemRenderer = RenderSlot;
            list.numItems = count; // 非虚拟列表会同步建出全部条目并回调渲染器
            RefreshWidth(list);
            IsBuilt = true;
        }

        // 重画内容：条目数不变，按新数据逐个刷新（恢复进度靠这条周期性推进）
        public void Refresh(IReadOnlyList<Slot> data, int selected)
        {
            if (!IsBuilt || m_content == null) return;
            selectedSlot = selected;
            slots.Clear();
            if (data != null)
                for (int i = 0; i < data.Count; i++) slots.Add(data[i]);
            m_content.numItems = SlotCount;
        }

        private void RenderSlot(int index, GObject obj)
        {
            if (obj is not UI_SkillListItem slot) return;
            Slot data = index < slots.Count ? slots[index] : new Slot(-1, 1f, 0, true);
            slot.Fill(data, index == selectedSlot);
        }

        // 技能图标：技能栏与技能选择面板共用同一张表
        internal static Texture ResolveIcon(int skillId)
        {
            if (skillId < 0) return null;
            var map = Tool.ResourceManager != null ? Tool.ResourceManager.SkillIconMap : null;
            if (map == null) return null;
            return map.TryGetValue(skillId, out var icon) ? icon : null; // 没配图标 → 不显示
        }

        // 列表宽度 = 全部条目宽度之和（FGUI 里是横向单行、不会自己撑开）
        private static void RefreshWidth(GList list)
        {
            float width = 0f;
            for (int i = 0; i < list.numChildren; i++)
            {
                var child = list.GetChildAt(i);
                if (child != null) width += child.width;
            }
            list.width = width;
        }
    }
}
