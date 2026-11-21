using System.Collections.Generic;
using FairyGUI;
using Ros.Transport;
using TerrainGen;
using UnityEngine;

using Ros.Config;
namespace Ros.UI.Main
{
    // 上帝视角（左 Ctrl）：显示区域 = 中心 + 边长（地形边长 / 倍率），可拖动；服务器只在收到请求时回包
    // 实体点位可点，点中后单独请求详情快照填右侧面板；珊瑚只画点、不响应点击
    // 点位按实体 id 复用：收到就移过去，超过 MarkerTimeout 没再收到就清掉（丢包只影响丢的那几条）
    public partial class UI_GodViewMinimapPanel
    {
        private const float RequestInterval = 0.2f; // 进界面期间按此频率反复拉取，本地图不动就不请求
        private const float MarkerTimeout = 3f;     // 实体超过此时长没再收到即从上帝视角清除
        private const int DetailSkillCapacity = 8;

        private struct Marker
        {
            public GObject view;
            public Vector2 world;
            public float lastSeen; // 收到该实体所在包时的客户端时间
        }

        private readonly Dictionary<ushort, Marker> entityMarkers = new(); // 可点实体
        private readonly Dictionary<ushort, Marker> coralMarkers = new();   // 珊瑚：只画点、不可点
        private readonly List<ushort> expiredScratch = new();
        private readonly List<UI_SkillList.Slot> detailSkills = new();
        private readonly List<UI_SkillList.Slot> detailHeldSkills = new();
        private readonly List<SCGodViewEntityDetail.Buff> detailBuffs = new();

        private NTexture mapTexture;
        private Vector2 center;
        private int scaleIndex;
        private float requestTimer;
        private bool dragging;
        private bool markersDirty;
        private Vector2 lastPointer;
        private float itemScale = 1f;
        private EntityCamp myCamp = EntityCamp.None;
        private ushort ownEntityId;

        public bool IsOpen => visible;

        // 悬浮提示由条目自发弹出，这里只绑拖拽
        public void Init()
        {
            m_largeMinimapContainer.onTouchBegin.Set(OnDragBegin);
            m_largeMinimapContainer.onTouchMove.Set(OnDragMove);
            m_largeMinimapContainer.onTouchEnd.Set(OnDragEnd);
        }

        public void Open(Vector2 worldCenter)
        {
            visible = true;
            touchable = true;
            m_largeMinimapContainer.opaque = true;
            m_largeMinimapContainer.touchable = true;
            center = ClampCenter(worldCenter);
            scaleIndex = 0; // 默认取配表首档（放大最多的一档）
            requestTimer = RequestInterval;
            dragging = false;
            markersDirty = false;
            DisposeMarkers(entityMarkers);
            DisposeMarkers(coralMarkers);
            ClearDetail();
            BuildScaleButtons();
            ComputeItemScale();
            ApplyLayout();
            SendRequest(); // 进界面立刻拉一次，不等第一个节拍
        }

        public void Close()
        {
            visible = false;
            touchable = false;
            dragging = false;
            markersDirty = false;
            DisposeMarkers(entityMarkers);
            DisposeMarkers(coralMarkers);
            ClearDetail();
        }

        public void Tick(float deltaTime, EntityCamp localCamp, ushort ownId)
        {
            if (!visible) return;
            myCamp = localCamp;
            ownEntityId = ownId;
            ExpireMarkers();
            if (markersDirty)
            {
                markersDirty = false;
                PlaceMarkers();
            }
            requestTimer -= deltaTime;
            if (requestTimer > 0f) return;
            requestTimer = RequestInterval;
            SendRequest();
        }

        #region 拖拽
        private void OnDragBegin(EventContext context)
        {
            if (!visible) return;
            dragging = true;
            lastPointer = m_largeMinimapContainer.GlobalToLocal(context.inputEvent.position);
            context.CaptureTouch();
        }

        private void OnDragMove(EventContext context)
        {
            if (!dragging) return;
            Vector2 current = m_largeMinimapContainer.GlobalToLocal(context.inputEvent.position);
            Vector2 delta = current - lastPointer;
            lastPointer = current;
            float ppm = PixelsPerMeter;
            if (ppm <= 0f) return;
            // 内容跟着手指走：向右拖 = 视野中心左移，向下拖 = 视野中心上移（世界 Z+）
            center = ClampCenter(new Vector2(center.x - delta.x / ppm, center.y + delta.y / ppm));
            ApplyLayout();
        }

        private void OnDragEnd(EventContext context) => dragging = false;
        #endregion

        #region 数据回包
        // 逐条落点位：已有该 id 就只更新坐标与收到时间，没有才新建；不做整轮拼装，单包丢失只影响包内几条
        public void OnEntities(SCGodViewEntities chunk)
        {
            if (!visible) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < chunk.entries.Count; i++)
            {
                var entry = chunk.entries[i];
                var view = EnsureEntityView(entry.entityId);
                if (view == null) continue;
                view.m_type.selectedIndex = UI_Minimap.PageOf(entry.type.category, entry.type, entry.camp, entry.entityId == ownEntityId, myCamp);
                entityMarkers[entry.entityId] = new Marker
                {
                    view = view,
                    world = new Vector2(entry.posX, entry.posZ),
                    lastSeen = now,
                };
            }
            markersDirty = true;
        }

        public void OnCorals(SCGodViewCorals chunk)
        {
            if (!visible) return;
            float now = Time.unscaledTime;
            for (int i = 0; i < chunk.points.Count; i++)
            {
                var point = chunk.points[i];
                var view = EnsureCoralView(point.entityId);
                if (view == null) continue;
                coralMarkers[point.entityId] = new Marker
                {
                    view = view,
                    world = new Vector2(point.posX, point.posZ),
                    lastSeen = now,
                };
            }
            markersDirty = true;
        }

        public void OnDetail(SCGodViewEntityDetail info)
        {
            if (!visible) return;
            if (info.entityId == 0)
            {
                ClearDetail(); // 实体已消失：收起详情
                return;
            }
            m_show_character_detail.selectedIndex = 1;
            TipHost.Hide(); // 换实体后旧提示内容已失效
            FillHead(info);
            FillHealth(info);
            FillBuffs(info);
            FillSkills(info);
        }

        private void SendRequest()
        {
            if (Tool.NetworkManager == null) return;
            float half = Edge * 0.5f;
            Tool.NetworkManager.SendGodViewArea(new CSGodViewArea
            {
                min = new Vector2(center.x - half, center.y - half),
                max = new Vector2(center.x + half, center.y + half),
            });
        }
        #endregion

        #region 点位
        private UI_MinimapItemButton EnsureEntityView(ushort entityId)
        {
            if (entityMarkers.TryGetValue(entityId, out var marker) && marker.view != null && !marker.view.isDisposed)
                return marker.view as UI_MinimapItemButton;
            var view = UI_MinimapItemButton.CreateInstance();
            view.scale = new Vector2(itemScale, itemScale);
            view.onClick.Set(() => RequestDetail(entityId));
            m_largeMinimapContainer.AddChild(view);
            return view;
        }

        private UI_MinimapItem EnsureCoralView(ushort entityId)
        {
            if (coralMarkers.TryGetValue(entityId, out var marker) && marker.view != null && !marker.view.isDisposed)
                return marker.view as UI_MinimapItem;
            var view = UI_MinimapItem.CreateInstance();
            view.scale = new Vector2(itemScale, itemScale);
            view.touchable = false;
            view.m_type.selectedIndex = UI_Minimap.PageOf(EntityCategory.Coral, default, EntityCamp.None, false, myCamp);
            m_largeMinimapContainer.AddChild(view);
            return view;
        }

        private void RequestDetail(ushort entityId)
        {
            if (!visible || Tool.NetworkManager == null) return;
            Tool.NetworkManager.SendGodViewEntityDetail(new CSGodViewEntityDetail { entityId = entityId });
        }

        // 超过 MarkerTimeout 没再收到（实体消失 / 拖出区域 / 服务端停发）就清点位
        private void ExpireMarkers()
        {
            float now = Time.unscaledTime;
            if (ExpireMarkers(entityMarkers, now)) markersDirty = true;
            if (ExpireMarkers(coralMarkers, now)) markersDirty = true;
        }

        private bool ExpireMarkers(Dictionary<ushort, Marker> markers, float now)
        {
            expiredScratch.Clear();
            foreach (var pair in markers)
                if (now - pair.Value.lastSeen > MarkerTimeout) expiredScratch.Add(pair.Key);
            if (expiredScratch.Count == 0) return false;
            for (int i = 0; i < expiredScratch.Count; i++) RemoveMarker(markers, expiredScratch[i]);
            return true;
        }

        private void PlaceMarkers()
        {
            if (m_largeMinimapContainer == null) return;
            float ppm = PixelsPerMeter;
            float minX = center.x - Edge * 0.5f;
            float maxZ = center.y + Edge * 0.5f;
            foreach (var marker in entityMarkers.Values) PlaceMarker(marker.view, marker.world, ppm, minX, maxZ);
            foreach (var marker in coralMarkers.Values) PlaceMarker(marker.view, marker.world, ppm, minX, maxZ);
        }

        // 点位组件的轴心在中心（pivotAsAnchor），xy 就是图标中点，不能再扣半宽高
        private static void PlaceMarker(GObject view, Vector2 world, float ppm, float minX, float maxZ)
        {
            if (view == null) return;
            view.SetXY((world.x - minX) * ppm, (maxZ - world.y) * ppm);
        }

        private static void DisposeMarkers(Dictionary<ushort, Marker> markers)
        {
            foreach (var marker in markers.Values)
                if (marker.view != null) marker.view.Dispose();
            markers.Clear();
        }

        private static void RemoveMarker(Dictionary<ushort, Marker> markers, ushort entityId)
        {
            if (!markers.TryGetValue(entityId, out var marker)) return;
            if (marker.view != null) marker.view.Dispose();
            markers.Remove(entityId);
        }
        #endregion

        #region 区域与底图
        private float Edge => GlobalVariable.RangeLength(global_variablesEnum.terrain_size) / GlobalVariable.L(global_variablesEnum.godView_scale)[scaleIndex];

        private float PixelsPerMeter
        {
            get
            {
                float edge = Edge;
                if (edge > 0f) return m_largeMinimapContainer.width / edge;
                return 0f;
            }
        }

        private Vector2 ClampCenter(Vector2 value)
        {
            float edge = Edge;
            float size = GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
            if (edge >= size) return new Vector2(size * 0.5f, size * 0.5f); // 区域不比地图小：居中
            float half = edge * 0.5f;
            return new Vector2(Mathf.Clamp(value.x, half, size - half), Mathf.Clamp(value.y, half, size - half));
        }

        // 底图与点位同一套换算：图左下 = 世界 (0,0)、上 = 世界 Z+
        private void ApplyLayout()
        {
            if (m_largeMinimapContainer == null) return;
            EnsureMapImage();
            float ppm = PixelsPerMeter;
            if (ppm <= 0f) return;
            float mapSize = GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
            float size = mapSize * ppm;
            var loader = m_largeMinimapContainer.m_loader;
            if (loader != null)
            {
                loader.SetSize(size, size);
                loader.SetXY((center.x - Edge * 0.5f) * -ppm, (center.y + Edge * 0.5f - mapSize) * ppm);
            }
            PlaceMarkers();
        }

        private void EnsureMapImage()
        {
            GLoader loader = null;
            if (m_largeMinimapContainer != null) loader = m_largeMinimapContainer.m_loader;
            if (loader == null) return;
            loader.fill = FillType.Scale;
            if (mapTexture == null)
            {
                Texture2D tex = TerrainMapImage.Load();
                if (tex != null) mapTexture = new NTexture(tex);
            }
            if (loader.texture == null) loader.texture = mapTexture;
        }

        // 抵消容器上层缩放回到设计尺寸，再乘配表的点位放大倍数（godView_marker_scale，便于点选）
        private void ComputeItemScale()
        {
            float upper = 1f;
            for (GObject o = m_largeMinimapContainer; o != null && o.parent != GRoot.inst; o = o.parent)
                if (o.scale.x > 0.0001f) upper *= o.scale.x;
            float design = 1f;
            if (upper > 0.0001f) design = 1f / upper;
            itemScale = design * GlobalVariable.F(global_variablesEnum.godView_marker_scale);
        }

        private void BuildScaleButtons()
        {
            var scales = GlobalVariable.L(global_variablesEnum.godView_scale);
            if (m_scaleButtonList == null) return;
            m_scaleButtonList.itemRenderer = (index, obj) =>
            {
                var button = obj.asButton;
                if (button == null) return;
                button.title = scales[index].ToString("0") + "x";
                button.selected = index == scaleIndex;
                int target = index;
                button.onClick.Set(() => SelectScale(target));
            };
            m_scaleButtonList.numItems = scales.Count;
        }

        private void SelectScale(int index)
        {
            if (index == scaleIndex) return;
            scaleIndex = index;
            center = ClampCenter(center);
            for (int i = 0; i < m_scaleButtonList.numChildren; i++)
            {
                var button = m_scaleButtonList.GetChildAt(i).asButton;
                if (button != null) button.selected = i == scaleIndex;
            }
            ApplyLayout();
            SendRequest(); // 换档立即拉一次，不等节拍
        }
        #endregion

        #region 详情面板
        private void ClearDetail()
        {
            TipHost.Hide();
            m_show_character_detail.selectedIndex = 0; // 0 = 收起 / 1 = 展开
            detailSkills.Clear();
            detailHeldSkills.Clear();
            detailBuffs.Clear();
            if (m_activeSkillList != null) m_activeSkillList.numItems = 0;
            if (m_skillList != null) m_skillList.numItems = 0;
            if (m_buff_list != null && m_buff_list.m_list != null) m_buff_list.m_list.numItems = 0;
            if (m_entity_name != null) m_entity_name.text = "";
            if (m_label_health != null) m_label_health.text = "";
        }

        private void FillHead(SCGodViewEntityDetail info)
        {
            bool isCharacter = info.type.category == EntityCategory.Character_Attack
                || info.type.category == EntityCategory.Character_Defense;
            List<Texture2D> icons = null;
            if (Tool.ResourceManager != null)
            {
                if (info.camp == EntityCamp.Defense) icons = Tool.ResourceManager.DefenseCharacterIcons;
                else icons = Tool.ResourceManager.AttackCharacterIcons;
            }
            int index = info.type.value;
            bool hasIcon = isCharacter && icons != null && index >= 0 && index < icons.Count && icons[index] != null;
            if (m_roleHead != null)
            {
                if (hasIcon) m_roleHead.m_headIcon.texture = new NTexture(icons[index]);
                else m_roleHead.m_headIcon.texture = null;
                if (hasIcon) m_roleHead.m_show_default_icon.selectedIndex = 0;
                else m_roleHead.m_show_default_icon.selectedIndex = 1;
                m_roleHead.m_level.selectedIndex = Mathf.Clamp(info.level - 1, 0, 4); // 头像控制器只有 5 档
            }
            if (m_entity_name != null) m_entity_name.text = info.name;
            if (m_campIcon != null) m_campIcon.m_player_camp.selectedIndex = (int)ToPlayerCamp(info.camp);
        }

        private void FillHealth(SCGodViewEntityDetail info)
        {
            float ratio = 0f;
            if (info.maxHealth > 0) ratio = Mathf.Clamp01((float)info.health / info.maxHealth);
            if (m_bar_health != null && m_bar_health.m_fill != null) m_bar_health.m_fill.fillAmount = ratio;
            if (m_label_health != null) m_label_health.text = $"{info.health}/{info.maxHealth}";
        }

        private void FillBuffs(SCGodViewEntityDetail info)
        {
            if (m_buff_list == null || m_buff_list.m_list == null) return;
            detailBuffs.Clear();
            for (int i = 0; i < info.buffs.Count; i++) detailBuffs.Add(info.buffs[i]);
            var list = m_buff_list.m_list;
            list.itemRenderer = (index, obj) =>
                ((UI_buffItem)obj).Fill(detailBuffs[index].type, detailBuffs[index].level);
            list.numItems = detailBuffs.Count;
        }

        // 技能栏槽位进池（带冷却进度与库存）；未装备的持有技能单独一栏，只画图标
        private void FillSkills(SCGodViewEntityDetail info)
        {
            detailSkills.Clear();
            for (int i = 0; i < info.slotSkills.Count; i++)
            {
                var slot = info.slotSkills[i];
                detailSkills.Add(new UI_SkillList.Slot(slot.skillId, slot.recharge01 / 100f, slot.store, slot.store < 0));
            }
            if (m_activeSkillList != null)
            {
                m_activeSkillList.itemRenderer = RenderSkillSlot;
                m_activeSkillList.numItems = detailSkills.Count;
            }

            detailHeldSkills.Clear();
            for (int i = 0; i < info.heldSkills.Count && i < DetailSkillCapacity; i++)
                detailHeldSkills.Add(new UI_SkillList.Slot(info.heldSkills[i], 1f, 0, true));
            if (m_skillList != null)
            {
                m_skillList.itemRenderer = RenderHeldSkillSlot;
                m_skillList.numItems = detailHeldSkills.Count;
            }
        }

        private void RenderSkillSlot(int index, GObject obj) => FillSkillItem(obj, detailSkills, index);

        private void RenderHeldSkillSlot(int index, GObject obj) => FillSkillItem(obj, detailHeldSkills, index);

        private static void FillSkillItem(GObject obj, List<UI_SkillList.Slot> source, int index)
        {
            if (obj is not UI_SkillListItem slot) return;
            UI_SkillList.Slot data = new UI_SkillList.Slot(-1, 1f, 0, true);
            if (index < source.Count) data = source[index];
            slot.Fill(data);
        }
        #endregion

        // 实体阵营 → 玩家阵营档位（PlayerCampIcon 页序：0 进攻 / 1 防守 / 2 观战 / 3 中立）
        private static PlayerCampType ToPlayerCamp(EntityCamp camp)
        {
            if (camp == EntityCamp.Attack) return PlayerCampType.Attack;
            if (camp == EntityCamp.Defense) return PlayerCampType.Defense;
            return PlayerCampType.Neutral;
        }
    }
}
