using System.Collections.Generic;
using FairyGUI;
using TerrainGen;
using UnityEngine;

using Ros.Config;
namespace Ros.UI.Main
{
    // 小地图展开（左 Alt）：整张地形图铺满 loader，把雷达当前收到的点位按整图换算铺上去，纯展示不可点
    public partial class UI_LargeMinimapPanel
    {
        private readonly Dictionary<ushort, UI_MinimapItem> items = new();
        private readonly Dictionary<int, UI_QuickMessagePointer> quickPointers = new();
        private readonly HashSet<ushort> staleIds = new();
        private readonly List<int> staleQuickIds = new();
        private NTexture mapTexture;
        private float quickPointerScale = 1f;

        public bool IsOpen => visible;

        public void Open()
        {
            EnsureMapImage();
            quickPointerScale = GlobalVariable.F(global_variablesEnum.quick_message_pointer_scale);
            visible = true;
            touchable = true;
        }

        public void Close()
        {
            visible = false;
            touchable = false;
            DisposeItems();
            ClearQuickMessages();
        }

        // 点位缓存每帧重排：数量不多，只做增删与换位，不重建已有图标
        public void Refresh(IReadOnlyDictionary<ushort, UI_Minimap.Point> points, EntityCamp myCamp, float selfYaw, bool hasSelfYaw)
        {
            if (!visible) return;
            EnsureMapImage();
            if (points == null)
            {
                DisposeItems();
                return;
            }

            staleIds.Clear();
            foreach (ushort id in items.Keys) staleIds.Add(id);
            foreach (var pair in points)
            {
                var point = pair.Value;
                var item = EnsureItem(point);
                if (item == null) continue;
                item.m_type.selectedIndex = UI_Minimap.PageOf(point.category, point.type, point.camp, point.self, myCamp);
                item.m_highlight.selectedIndex = point.marked ? 1 : 0;
                if (point.self && hasSelfYaw) item.rotation = selfYaw;
                Place(item, point);
                staleIds.Remove(point.id);
            }
            foreach (ushort id in staleIds) Remove(id);
        }

        // 快捷指令点位：整图换算直接落点，整张地图都在视野内，不需要夹边
        public void RefreshQuickMessages(IReadOnlyList<UI_Minimap.QuickMark> marks)
        {
            if (!visible) return;
            if (marks == null || marks.Count == 0)
            {
                ClearQuickMessages();
                return;
            }
            var loader = m_loader;
            if (loader == null) return;

            staleQuickIds.Clear();
            foreach (int id in quickPointers.Keys) staleQuickIds.Add(id);
            float mapSize = GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
            float ppmX = loader.width / mapSize;
            float ppmY = loader.height / mapSize;
            foreach (var mark in marks)
            {
                var pointer = EnsureQuickPointer(mark.id);
                if (pointer == null) continue;
                pointer.Bind(mark.messageId);
                pointer.Place(loader.x + mark.world.x * ppmX, loader.y + (mapSize - mark.world.y) * ppmY);
                staleQuickIds.Remove(mark.id);
            }
            foreach (int id in staleQuickIds) RemoveQuickPointer(id);
        }

        public void ClearQuickMessages()
        {
            foreach (var pointer in quickPointers.Values)
                if (pointer != null) pointer.DestroySelf();
            quickPointers.Clear();
        }

        private UI_QuickMessagePointer EnsureQuickPointer(int id)
        {
            if (quickPointers.TryGetValue(id, out var pointer) && pointer != null && !pointer.isDisposed) return pointer;
            pointer = UI_QuickMessagePointer.CreateInstance();
            pointer.scale = new Vector2(quickPointerScale, quickPointerScale);
            AddChild(pointer); // 后加的点位压在底图与实体点位之上
            quickPointers[id] = pointer;
            return pointer;
        }

        private void RemoveQuickPointer(int id)
        {
            if (!quickPointers.TryGetValue(id, out var pointer)) return;
            if (pointer != null) pointer.DestroySelf();
            quickPointers.Remove(id);
        }

        // 整图换算：图左下 = 世界 (0,0)、上 = 世界 Z+，loader 就是整张地图的显示区域
        private void Place(UI_MinimapItem item, in UI_Minimap.Point point)
        {
            if (m_loader == null) return;
            float mapSize = GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
            float ppmX = m_loader.width / mapSize;
            float ppmY = m_loader.height / mapSize;
            // 轴心与缩放决定 xy 到中点的偏移（见 UI_Minimap.CenterOffset），漏算会整体斜着偏
            Vector2 half = UI_Minimap.CenterOffset(item);
            item.SetXY(m_loader.x + point.world.x * ppmX - half.x,
                m_loader.y + (mapSize - point.world.y) * ppmY - half.y);
        }

        private UI_MinimapItem EnsureItem(in UI_Minimap.Point point)
        {
            if (items.TryGetValue(point.id, out var item) && item != null && !item.isDisposed) return item;
            item = UI_MinimapItem.CreateInstance();
            AddChild(item); // loader 是 1 号子物体，后加的点位天然压在底图之上
            items[point.id] = item;
            return item;
        }

        private void Remove(ushort id)
        {
            if (!items.TryGetValue(id, out var item)) return;
            if (item != null) item.Dispose();
            items.Remove(id);
        }

        private void DisposeItems()
        {
            foreach (var item in items.Values)
                if (item != null) item.Dispose();
            items.Clear();
        }

        private void EnsureMapImage()
        {
            if (m_loader == null) return;
            m_loader.fill = FillType.Scale;
            if (mapTexture == null)
            {
                Texture2D tex = TerrainMapImage.Load();
                if (tex != null) mapTexture = new NTexture(tex);
            }
            if (m_loader.texture == null) m_loader.texture = mapTexture;
        }
    }
}
