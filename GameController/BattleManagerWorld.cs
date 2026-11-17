using System.Collections.Generic;
using Ros.Transport;
using TerrainGen;
using UnityEngine;

using Ros.Config;
public partial class BattleManager
{
    private Ticker coralSpawnTicker;
    private Ticker zombieSpawnTicker;
    private static readonly List<Vector3> s_playerPosScratch = new();
    private static readonly List<Vector3> s_coralPosScratch = new();
    private static readonly List<ushort> s_coralIdScratch = new();

    private float neutralStatueRespawnTime = -1f;

    private void SpawnBattleWorld()
    {
        if (!TerrainSpawns.Built)
        {
            Debug.LogError("[地形] TerrainSpawns 未构建：地形还没生成就进入战斗世界初始化，雕像与出生点无法定位");
            return;
        }

        // 副守护点：index 即 value 0~2（贤者雕像），统一面向主守护点
        for (int i = 0; i < TerrainSpawns.SubGuards.Count; i++)
        {
            ushort id = SpawnEntity(EntityType.GuardStatue(i), 1, TerrainSpawns.SubGuards[i], EntityCamp.Defense);
            FacePoint(id, TerrainSpawns.MainGuard);
        }

        // 主守护点：国王雕像，面向进攻方出生点
        FacePoint(SpawnEntity(EntityType.KingStatue, 1, TerrainSpawns.MainGuard, EntityCamp.Defense), TerrainSpawns.AttackSpawn);

        // 珊瑚：开局不生成，按玩家邻近采样合法位置动态生成（见 TickCoralSpawn）

        // 防御塔（水晶雕像）：烘焙数据里每个守护点 3 个塔位；朝向取最近的守护点，故不需要知道塔位归属哪个守护点
        for (int i = 0; i < TerrainSpawns.Towers.Count; i++)
        {
            ushort id = SpawnEntity(EntityType.CrystalStatue(i), 1, TerrainSpawns.Towers[i], EntityCamp.Defense);
            FacePoint(id, NearestGuard(TerrainSpawns.Towers[i]));
        }

        // 中立雕像（中立争抢单位）：开局不刷，开战后按延迟计时刷新；之后由被打死的时机重排
        neutralStatueRespawnTime = Time.time + GlobalConfig.neutral_statue_first_spawn_delay;

        CaptureGuardTotalHealth(); // 守护点总血量在此刻定死，经验增幅按已扣除比例算
    }

    // 防御塔/守护点的初始血量人数修正：进攻方人数 /（防守方人数 × 2），夹 1~10 倍
    public float GetBuildingHealthScale(EntityCategory category)
    {
        if (category != EntityCategory.CrystalStatue && category != EntityCategory.GuardStatue) return 1f;
        int attack = 0, defense = 0;
        foreach (var pair in PlayerCamps)
        {
            if (pair.Value == PlayerCampType.Attack) attack++;
            else if (pair.Value == PlayerCampType.Defense) defense++;
        }
        if (attack <= 0 || defense <= 0) return 1f; // 不在对局中生成建筑（GM 直接生成）不做人数修正
        return Mathf.Clamp(attack / (defense * 2f), 1f, 10f);
    }

    #region 生成后摆方向
    // 生成完实体再单独摆方向：只写 Y 轴，不平移；两点重合即 0 度（不用 LookRotation，避开零向量报错）
    private static void FacePoint(ushort id, Vector3 point)
    {
        if (id == 0) return; // 模板缺失，SpawnEntity 已告警
        var entity = GetEntity(id);
        Vector3 dir = point - entity.transform.position;
        entity.transform.rotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
    }

    // 最近的守护点（主 + 副全体，XZ 距离）：防御塔朝向用，不必区分塔位属于哪个守护点
    private static Vector3 NearestGuard(Vector3 pos)
    {
        Vector3 nearest = TerrainSpawns.MainGuard;
        float nearestSqr = SqrDistanceXZ(pos, nearest);
        for (int i = 0; i < TerrainSpawns.SubGuards.Count; i++)
        {
            float sqr = SqrDistanceXZ(pos, TerrainSpawns.SubGuards[i]);
            if (sqr >= nearestSqr) continue;
            nearestSqr = sqr;
            nearest = TerrainSpawns.SubGuards[i];
        }
        return nearest;
    }

    private static float SqrDistanceXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return dx * dx + dz * dz;
    }
    #endregion

    private void SpawnZombie()
    {
        if (!TrySampleZombieSpawnPoint(out Vector3 pos)) return;
        int variant = Random.Range(0, GlobalConfig.zombie_variant_count);
        SpawnEntity(EntityType.Zombie(variant), GetZombieSpawnLevel(), pos, EntityCamp.Defense);
    }

    // 每满一个间隔做一批尝试：夜间用快速档次数、白天用慢速档次数；场上达上限整批跳过
    private void TickZombieSpawn()
    {
        if (!zombieSpawnTicker) return;

        int max = GlobalVariable.I(global_variablesEnum.zombie_max);
        if (zombieCount >= max) return;

        int times = IsDay
            ? GlobalVariable.I(global_variablesEnum.zombie_refresh_rate_slow)
            : GlobalVariable.I(global_variablesEnum.zombie_refresh_rate_fast);
        for (int i = 0; i < times && zombieCount < max; i++) SpawnZombie();
    }

    #region 环带采样（僵尸与珊瑚共用：以某个存活玩家为圆心，离所有存活玩家不小于内径）

    private const int RingSampleAttempts = 8;

    // 收集全部存活玩家位置（玩家数很少，一次收集供本次检测复用；阵亡/复活等待中取不到实体）
    private void CollectAlivePlayerPositions()
    {
        s_playerPosScratch.Clear();
        foreach (var clientId in PlayerInfoList.Keys)
        {
            var e = GetEntityOfClient(clientId);
            if (e == null || !e.Alive) continue;
            s_playerPosScratch.Add(e.transform.position);
        }
    }

    // 环带内取一个候选点：离所有存活玩家都不小于 minDist，避免贴脸刷新
    private bool TrySampleRingPoint(Vector3 center, float minDist, float maxDist, out Vector3 pos)
    {
        pos = default;
        if (!TerrainSpawns.TrySampleSpawnPointInRing(center, minDist, maxDist, out pos)) return false;
        return !IsAnyPlayerWithin(pos, minDist);
    }

    // 僵尸出生点：每次尝试随机挑一个存活玩家作圆心；无存活玩家时退回全图均匀采样
    private bool TrySampleZombieSpawnPoint(out Vector3 pos)
    {
        CollectAlivePlayerPositions();
        if (s_playerPosScratch.Count == 0) return TerrainSpawns.TrySampleSpawnPoint(out pos);

        float minDist = GlobalVariable.RangeMin(global_variablesEnum.zombie_spawn_range);
        float maxDist = GlobalVariable.RangeMax(global_variablesEnum.zombie_spawn_range);
        for (int attempt = 0; attempt < RingSampleAttempts; attempt++)
        {
            Vector3 center = s_playerPosScratch[Random.Range(0, s_playerPosScratch.Count)];
            if (TrySampleRingPoint(center, minDist, maxDist, out pos)) return true;
        }
        pos = default;
        return false;
    }

    #endregion

    private void SpawnNeutralStatue()
    {
        var list = TerrainSpawns.NeutralStatues;
        if (list.Count == 0) return; // 没有中立刷新点则不刷
        // 白天刷天使雕像、夜晚刷恶魔雕像（两者行为一致，只有外观与 value 不同）
        EntityType statue = IsDay ? EntityType.AngelStatue : EntityType.DemonStatue;
        SpawnEntity(statue, 1, TerrainSpawns.RandomOf(list), EntityCamp.Neutral);

        // 中立雕像刷新播报（首次出生与重生都算刷新）
        Tool.NetworkManager.SendBattleEvent(new SCBattleEvent()
        {
            type = SCBattleEvent.Type.NeutralStatueRespawn,
            textId = (int)system_messageEnum.neutral_statue_respawn,
        });
    }

    public void ScheduleNeutralStatueRespawn()
    {
        neutralStatueRespawnTime = Time.time + GlobalConfig.neutral_statue_respawn_delay;
    }

    public void NotifyNeutralStatueCaptured(ushort treeId)
    {
        Tool.NetworkManager.SendBattleEvent(new SCBattleEvent()
        {
            type = SCBattleEvent.Type.NeutralStatueCaptured,
            textId = (int)system_messageEnum.neutral_statue_captured,
        });
    }

    #region 珊瑚（按玩家邻近动态生成）

    private void TickCoralSpawn()
    {
        if (!coralSpawnTicker) return;

        DestroyFarCorals(); // 先清掉离所有存活玩家都超过 coral_kill_distance 的，再补刷

        // 场上珊瑚数超过阈值用慢速档的次数，否则用快速档
        int times = EntityContainer.Corals.Count > GlobalVariable.I(global_variablesEnum.coral_spawn_rate_rate)
            ? GlobalVariable.I(global_variablesEnum.coral_spawn_rate_slow)
            : GlobalVariable.I(global_variablesEnum.coral_spawn_rate_fast);
        for (int i = 0; i < times; i++) TrySpawnOneCoral();
    }

    // 离最近存活玩家超过 coral_kill_distance 的珊瑚直接移除：不回血、不计分
    private void DestroyFarCorals()
    {
        CollectAlivePlayerPositions();
        if (s_playerPosScratch.Count == 0) return; // 没有存活玩家就没有"最近玩家"，不清场

        float killDistance = GlobalVariable.F(global_variablesEnum.coral_kill_distance);
        s_coralIdScratch.Clear();
        foreach (var entity in EntityContainer.Corals)
        {
            if (entity == null || !entity.Alive) continue;
            if (IsAnyPlayerWithin(entity.transform.position, killDistance)) continue;
            s_coralIdScratch.Add(entity.id); // 先收集 id：销毁会改容器，不能在遍历中删
        }
        for (int i = 0; i < s_coralIdScratch.Count; i++) DestroyEntity(s_coralIdScratch[i]);
    }

    private void TrySpawnOneCoral()
    {
        CollectAlivePlayerPositions();
        if (s_playerPosScratch.Count == 0) return;

        // 场上已有珊瑚位置，用于保持最小间距
        s_coralPosScratch.Clear();
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity == null || !entity.Alive || entity.type.category != EntityCategory.Coral) continue;
            s_coralPosScratch.Add(entity.transform.position);
        }

        Vector3 center = s_playerPosScratch[Random.Range(0, s_playerPosScratch.Count)];
        float minDist = GlobalVariable.RangeMin(global_variablesEnum.coral_spawn_range);
        float maxDist = GlobalVariable.RangeMax(global_variablesEnum.coral_spawn_range);
        float spacing = GlobalVariable.F(global_variablesEnum.coral_spawn_spacing);
        float maxDensity = GlobalVariable.F(global_variablesEnum.coral_spawn_max_density);

        for (int attempt = 0; attempt < RingSampleAttempts; attempt++)
        {
            // 采样：所选玩家的环带内 + 离所有存活玩家不小于内径 + 向下射线命中地形上表面
            if (!TrySampleRingPoint(center, minDist, maxDist, out Vector3 pos)) continue;
            if (IsAnyCoralWithin(pos, spacing)) continue;               // 与已有珊瑚的最小间距
            if (CoralDensityAt(pos) >= maxDensity) continue;            // 所在区块珊瑚密度已达上限

            EntityType type = Random.value < GlobalVariable.F(global_variablesEnum.coral_spawn_as_large_chance) ? EntityType.CoralLarge : EntityType.CoralSmall;
            SpawnEntity(type, 1, pos, EntityCamp.Neutral);
            return;
        }
    }

    // 候选点所在区块的珊瑚平均密度（个/㎡）：区块面积按整格 chunkSize² 算，不剔除区块内的非法区
    private static float CoralDensityAt(Vector3 pos)
    {
        int count = EntityContainer.Corals.GetChunkCount(ChunkSearcher<EntityData>.GetChunkIndex(pos));
        float chunkSize = GlobalVariable.F(global_variablesEnum.terrain_chunk_size);
        return count / (chunkSize * chunkSize);
    }

    private static bool IsAnyPlayerWithin(Vector3 pos, float radius)
    {
        float rSq = radius * radius;
        for (int i = 0; i < s_playerPosScratch.Count; i++)
        {
            Vector3 p = s_playerPosScratch[i];
            float dx = p.x - pos.x;
            float dz = p.z - pos.z;
            if (dx * dx + dz * dz <= rSq) return true;
        }
        return false;
    }

    private static bool IsAnyCoralWithin(Vector3 pos, float radius)
    {
        float rSq = radius * radius;
        for (int i = 0; i < s_coralPosScratch.Count; i++)
        {
            Vector3 p = s_coralPosScratch[i];
            float dx = p.x - pos.x;
            float dz = p.z - pos.z;
            if (dx * dx + dz * dz <= rSq) return true;
        }
        return false;
    }
    #endregion

    private void TickWorldRespawn()
    {
        // 中立雕像：倒计时到期刷新一棵（同时只有一棵，故用单值时刻而不进列表）
        if (neutralStatueRespawnTime >= 0f && Time.time >= neutralStatueRespawnTime)
        {
            neutralStatueRespawnTime = -1f;
            SpawnNeutralStatue();
        }
    }

    #region 掉落回收（y < 0 的实体落回 Terrain 表面上方）

    private Ticker fallCheckTicker;

    private void TickFallRecover()
    {
        if (!fallCheckTicker) return;

        foreach (var entity in EntityContainer.Entities)
        {
            if (entity == null || entity.rb == null || !entity.Alive) continue; // 无刚体的雕像/珊瑚不会掉落
            Vector3 pos = entity.transform.position;
            if (pos.y >= 0f) continue;
            if (!TerrainSpawns.TrySampleGroundY(pos.x, pos.z, out float groundY)) continue;

            pos.y = groundY + GlobalConfig.entity_fall_recover_height;
            entity.transform.position = pos;
            entity.rb.position = pos;
            Vector3 velocity = entity.rb.velocity;
            velocity.y = 0f;
            entity.rb.velocity = velocity;
        }
    }

    #endregion
}
