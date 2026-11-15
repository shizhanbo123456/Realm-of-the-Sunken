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
        private readonly HashSet<ushort> staleIds = new();
        private NTexture mapTexture;

        public bool IsOpen => visible;

        public void Open()
        {
            EnsureMapImage();
            visible = true;
            touchable = true;
        }

        public void Close()
        {
            visible = false;
            touchable = false;
            DisposeItems();
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
