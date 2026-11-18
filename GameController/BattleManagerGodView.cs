using System.Collections.Generic;
using Ros.Transport;
using UnityEngine;

using Ros.Config;
public partial class BattleManager
{
    // 上帝视角：客户端按 0.2s 反复请求显示区域，服务器只在收到请求时扫描并回块，不保存状态、不做定时下发
    // 区域内的实体没有任何显示条件（阵营 / 视野 / 昼夜 / 显示距离一律不过滤）
    private static readonly List<SCMinimapEntity> s_godViewEntities = new();
    private static readonly List<SCGodViewCorals.Point> s_godViewCorals = new();

    public void ReceiveGodViewArea(int clientId, CSGodViewArea request)
    {
        if (!AtServer || !BattleStarted) return;

        // 包围盒必须是有限值且有面积；超出地图的部分按地形口径夹回边界
        if (!IsFinite(request.min) || !IsFinite(request.max)) return;
        float size = GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
        float minX = Mathf.Clamp(Mathf.Min(request.min.x, request.max.x), 0f, size);
        float minZ = Mathf.Clamp(Mathf.Min(request.min.y, request.max.y), 0f, size);
        float maxX = Mathf.Clamp(Mathf.Max(request.min.x, request.max.x), 0f, size);
        float maxZ = Mathf.Clamp(Mathf.Max(request.min.y, request.max.y), 0f, size);
        if (maxX <= minX || maxZ <= minZ) return;

        CollectGodViewEntities(minX, minZ, maxX, maxZ);
        SendGodViewEntities(clientId);
        SendGodViewCorals(clientId);
        s_godViewEntities.Clear();
        s_godViewCorals.Clear();
    }

    public void ReceiveGodViewEntityDetail(int clientId, CSGodViewEntityDetail request)
    {
        if (!AtServer || !BattleStarted) return;
        Tool.NetworkManager.SendGodViewEntityDetail(clientId, BuildGodViewDetail(request.entityId));
    }

    private void CollectGodViewEntities(float minX, float minZ, float maxX, float maxZ)
    {
        s_godViewEntities.Clear();
        s_godViewCorals.Clear();
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity == null) continue;
            var pos = entity.transform.position;
            if (pos.x < minX || pos.x > maxX || pos.z < minZ || pos.z > maxZ) continue;
            // 珊瑚数量最多，只进珊瑚流（id + 坐标，客户端按 id 落点），实体流不带
            if (entity.type.category == EntityCategory.Coral)
            {
                s_godViewCorals.Add(new SCGodViewCorals.Point
                {
                    entityId = entity.id,
                    posX = pos.x,
                    posZ = pos.z,
                });
                continue;
            }
            s_godViewEntities.Add(new SCMinimapEntity
            {
                entityId = entity.id,
                type = entity.type,
                camp = entity.camp,
                posX = pos.x,
                posZ = pos.z,
                marked = IsMarkedOnMinimap(entity),
            });
        }
    }

    // 按固定条数切包（GodViewChunk 写死的单包预算）；区域内无实体就一个包都不发，客户端靠 3s 超时清点
    private void SendGodViewEntities(int clientId)
    {
        int capacity = GodViewChunk.EntityCapacity;
        int total = s_godViewEntities.Count;
        for (int start = 0; start < total; start += capacity)
        {
            var chunk = new SCGodViewEntities { entries = new List<SCMinimapEntity>() };
            int end = Mathf.Min(start + capacity, total);
            for (int i = start; i < end; i++) chunk.entries.Add(s_godViewEntities[i]);
            Tool.NetworkManager.SendGodViewEntities(clientId, chunk);
        }
    }

    private void SendGodViewCorals(int clientId)
    {
        int capacity = GodViewChunk.CoralCapacity;
        int total = s_godViewCorals.Count;
        for (int start = 0; start < total; start += capacity)
        {
            var chunk = new SCGodViewCorals { points = new List<SCGodViewCorals.Point>() };
            int end = Mathf.Min(start + capacity, total);
            for (int i = start; i < end; i++) chunk.points.Add(s_godViewCorals[i]);
            Tool.NetworkManager.SendGodViewCorals(clientId, chunk);
        }
    }

    // 点选详情：实体已消失时回 entityId = 0，客户端收起详情面板
    private SCGodViewEntityDetail BuildGodViewDetail(ushort entityId)
    {
        if (!EntityContainer.Entities.TryGetObject(entityId, out var entity) || entity == null)
            return new SCGodViewEntityDetail
            {
                buffs = new List<SCGodViewEntityDetail.Buff>(),
                slotSkills = new List<SCGodViewEntityDetail.SlotSkill>(),
                heldSkills = new List<ushort>(),
            };
        var info = new SCGodViewEntityDetail
        {
            entityId = entity.id,
            type = entity.type,
            camp = entity.camp,
            level = entity.level,
            name = ResolveGodViewName(entity),
            health = Mathf.Max(0, (int)entity.attributes.Health),
            maxHealth = (int)entity.attributes.MaxHealth,
            buffs = new List<SCGodViewEntityDetail.Buff>(),
            slotSkills = new List<SCGodViewEntityDetail.SlotSkill>(),
            heldSkills = new List<ushort>(),
        };
        if (entity.skillController != null) entity.skillController.FillGodViewSkills(ref info);
        FillGodViewBuffs(entity, ref info);
        return info;
    }

    // buff 复用实体表现包的同一份数据（effectController 的运行时快照），条数按同一口径截断
    private static void FillGodViewBuffs(EntityData entity, ref SCGodViewEntityDetail info)
    {
        var display = entity.GetDisplayInfo(true);
        int max = SyncRanges.MaxBuffCount; // global_variables.sync_max_buff_count
        foreach (var buff in display.buffs)
        {
            if (info.buffs.Count >= max) break;
            info.buffs.Add(new SCGodViewEntityDetail.Buff
            {
                type = (byte)buff.type,
                level = (byte)buff.level,
            });
        }
    }

    // 玩家单位取玩家名（真人与人机都登记在 PlayerInfoList），其它实体取 entity.csv 的名字
    private string ResolveGodViewName(EntityData entity)
    {
        foreach (var pair in PlayerEntityId)
        {
            if (pair.Value != entity.id) continue;
            if (PlayerInfoList.TryGetValue(pair.Key, out var player) && !string.IsNullOrEmpty(player.name)) return player.name;
            break;
        }
        return EntityConfig.GetByCategory(entity.type).name;
    }

    private static bool IsFinite(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y)
        && !float.IsInfinity(value.x) && !float.IsInfinity(value.y);
}
