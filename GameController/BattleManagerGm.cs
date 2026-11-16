using System.Collections.Generic;
using Ros.Config;
using Ros.Gm;
using Ros.Transport;
using TerrainGen;
using UnityEngine;

// GM 指令的服务器执行体：NetworkManager.ServerReceiveGmLocal → ExecuteGm
// 指令 id 与参数含义见配表 gm_command；纯客户端项（敌方聊天/雾气/结算演示/客户端调试）在 GmPanel 本地执行
public partial class BattleManager
{
    // 击退指令的固定作用半径
    private const float gmKickRadius = 5f;

    // 无敌实体（EntityData.OnDamaged 命中即按 0 伤害）
    private static readonly HashSet<ushort> gmInvincible = new();

    public static bool IsGmInvincible(ushort entityId) => gmInvincible.Contains(entityId);

    // 开战钩子（由 ApplyGlobalPassives 调用）：清掉上一局的无敌记录（实体 id 会被复用）
    private void GmOnBattleStart()
    {
        gmInvincible.Clear();
    }

    public void ExecuteGm(int clientId, CSGm gm)
    {
        if (!AtServer) return;
        Debug.Log($"[GM] {gm.type}(i={gm.i},v={gm.v0},{gm.v1},{gm.v2}) client={clientId}");

        switch (gm.type)
        {
            // ===== 对局 =====
            case GmCommandId.TimeAdd: GmSetRemainTime(BattleRemainTime + gm.i); break;
            case GmCommandId.TimeReduce: GmSetRemainTime(BattleRemainTime - gm.i); break;
            case GmCommandId.TimeSet: GmSetRemainTime(gm.i); break;
            case GmCommandId.End:
                if (!BattleStarted) break;
                EndBattle(AttackScore >= DefenseScore() ? 1 : 2); // 时间结束：按比分
                break;
            case GmCommandId.SetDayTime:
                // 0~1 全天进度 → 周期值 [0,2)
                SetCycleTime(gm.v0 * DayNightCycleLength);
                break;
            case GmCommandId.SetDayLength:
                dayDuration = Mathf.Max(0.01f, gm.i);
                break;
            case GmCommandId.SetNightLength:
                nightDuration = Mathf.Max(0.01f, gm.i);
                break;

            // ===== 实体 =====
            case GmCommandId.SpawnZombie:
                if (!BattleStarted) break;
                for (int k = 0; k < gm.i; k++)
                    GmSpawnAtPlayer(clientId, EntityType.Zombie(Random.Range(0, GlobalConfig.zombie_variant_count)),
                        GetZombieSpawnLevel(), EntityCamp.Defense);
                break;
            case GmCommandId.SpawnEliteZombie:
                if (!BattleStarted) break;
                for (int k = 0; k < gm.i; k++)
                    GmSpawnAtPlayer(clientId, EntityType.EliteZombie(Random.Range(0, GlobalConfig.elite_zombie_variant_count)),
                        GetZombieSpawnLevel(), EntityCamp.Defense);
                break;
            case GmCommandId.SpawnAngelStatue: GmSpawnNeutralStatue(EntityType.AngelStatue); break;
            case GmCommandId.SpawnDevilStatue: GmSpawnNeutralStatue(EntityType.DemonStatue); break;
            case GmCommandId.SpawnTower: GmSpawnTowers(gm.i); break;

            case GmCommandId.Kick: GmKick(clientId, gm.v0); break;

            // ===== 玩家 =====
            case GmCommandId.Tp: GmTeleport(clientId, new Vector3(gm.v0, gm.v1, gm.v2)); break;
            case GmCommandId.TpTo: GmTeleportToGuardPoint(clientId, gm.i); break;
            case GmCommandId.Respawn: GmRevive(clientId); break;
            case GmCommandId.GetSkill: GmAddSkill(clientId, gm.i); break;
            case GmCommandId.UseSkill: GmForceUseSkill(clientId, gm.i); break;
            case GmCommandId.ClearSkill: GmClearSkill(clientId); break;
            case GmCommandId.AddBuff: GmAddBuff(clientId, (EffectType)gm.i); break;
            case GmCommandId.ClearBuff: GmRemoveAllBuff(clientId); break;
            case GmCommandId.Fly:
                if (GetGmPlayerEntity(clientId) is PlayerEntityData flight) flight.SetFlightMode(gm.i != 0);
                break;
            case GmCommandId.Invincible: GmSetInvincible(clientId, gm.i != 0); break;
            case GmCommandId.ExtremeSpeed:
                {
                    var effect = GetGmEffectController(clientId);
                    if (effect == null) break;
                    if (gm.i != 0) effect.AddEffect(EffectType.GmSpeedUp);
                    else effect.RemoveEffect(EffectType.GmSpeedUp);
                    break;
                }

            // ===== 服务器查询 =====
            case GmCommandId.ServerFps:
                SendGmResult(clientId, gm.type, i: EnsInstance.frameRate);
                break;
            case GmCommandId.ServerTime:
                SendGmResult(clientId, gm.type, v: Time.time);
                break;
            case GmCommandId.ServerEntityCount:
                SendGmResult(clientId, gm.type, i: GmCountAliveEntities());
                break;
            case GmCommandId.ServerDayLength:
                SendGmResult(clientId, gm.type, v: dayDuration);
                break;
            case GmCommandId.ServerNightLength:
                SendGmResult(clientId, gm.type, v: nightDuration);
                break;
            case GmCommandId.ServerZombieSpawnLevel:
                SendGmResult(clientId, gm.type, i: GetZombieSpawnLevel());
                break;

            default:
                Debug.LogWarning($"[GM] 服务器未处理的指令 id={gm.type}（可能是纯客户端项被误发）");
                break;
        }
    }

    private static void SendGmResult(int clientId, int type, int i = 0, float v = 0f)
    {
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendGm(clientId, new SCGm { type = type, i = i, v0 = v });
    }

    #region//Local
    // 剩余时间：计时分支只在 >0 时推进，所以下限压到 0.01，让它在下一帧自然结算
    private void GmSetRemainTime(float seconds)
    {
        if (!BattleStarted) return;
        BattleRemainTime = Mathf.Max(0.01f, seconds);
    }

    private EntityData GetGmPlayerEntity(int clientId) =>
        PlayerEntityId.TryGetValue(clientId, out var id) ? GetEntity(id) : null;

    private EntitySkillController GetGmSkillController(int clientId)
    {
        var owner = GetGmPlayerEntity(clientId);
        return owner != null ? owner.skillController : null;
    }

    private EntityEffectController GetGmEffectController(int clientId)
    {
        var owner = GetGmPlayerEntity(clientId);
        return owner != null ? owner.effectController : null;
    }

    // 技能 / buff：GM 指令作用于发送者自己，取不到实体就静默跳过
    private void GmAddSkill(int clientId, int skillId)
    {
        var skills = GetGmSkillController(clientId);
        if (skills == null) return;
        skills.AddSkill(skillId);
        var held = PlayerSkillList(clientId);
        if (!held.Contains(skillId)) held.Add(skillId); // 玩家级持有表同步，死亡复活后仍在
        var sync = new SCSkillSync { full = false };
        sync.skills.Add(skillId);
        Tool.NetworkManager.SendSkillSync(clientId, sync);
        SendSkillRecharge(clientId); // 新技能若自动进栏，槽位与根戳要一起补
    }

    private void GmForceUseSkill(int clientId, int skillId)
    {
        var skills = GetGmSkillController(clientId);
        if (skills != null) skills.GmForceUseSkill(skillId);
    }

    private void GmClearSkill(int clientId)
    {
        var skills = GetGmSkillController(clientId);
        if (skills == null) return;
        skills.SetSkillList(null);
        ClearPlayerSkills(clientId); // 玩家级持有表一起清，否则复活会把技能带回来
        Tool.NetworkManager.SendSkillSync(clientId, new SCSkillSync { full = true });
        SendSkillRecharge(clientId);
    }

    private void GmAddBuff(int clientId, EffectType type)
    {
        var effect = GetGmEffectController(clientId);
        if (effect != null) effect.AddEffect(type);
    }

    private void GmRemoveAllBuff(int clientId)
    {
        var effect = GetGmEffectController(clientId);
        if (effect != null) effect.RemoveAll();
    }

    private void GmSetInvincible(int clientId, bool on)
    {
        var target = GetGmPlayerEntity(clientId);
        if (target == null) return;
        if (on) gmInvincible.Add(target.id);
        else gmInvincible.Remove(target.id);
    }

    // 传送：直接落点，并清速度与位移，避免刚体/位移效果把角色拉回
    private void GmTeleport(int clientId, Vector3 pos)
    {
        var target = GetGmPlayerEntity(clientId);
        if (target == null) return;
        target.transform.position = pos;
        if (target.rb != null)
        {
            target.rb.position = pos;
            target.rb.velocity = Vector3.zero;
        }
        target.motion = null;
    }

    // 0 = 主守护点（国王雕像），1~3 = 副守护点（贤者雕像 0~2）
    private void GmTeleportToGuardPoint(int clientId, int index)
    {
        EntityType want = index <= 0 ? EntityType.KingStatue : EntityType.GuardStatue(index - 1);
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue == null || guardStatue.type != want) continue;
            GmTeleport(clientId, guardStatue.transform.position);
            return;
        }
        Debug.LogWarning($"[GM] 场上没有目标守护点（{index}），传送取消");
    }

    private void GmRevive(int clientId)
    {
        if (!BattleStarted) return;
        if (PlayerEntityId.ContainsKey(clientId)) return; // 还活着
        if (!reviveStates.TryGetValue(clientId, out var rs))
        {
            rs = new ReviveState();
            reviveStates[clientId] = rs;
        }
        RespawnPlayer(clientId, rs);
    }

    // 刷新 1 个：落点 = 令牌对应玩家实体的 x/z（高度吸附地形，人形抬升由 SpawnEntity 统一处理）
    private void GmSpawnAtPlayer(int clientId, EntityType type, int level, EntityCamp camp)
    {
        var owner = GetGmPlayerEntity(clientId);
        if (owner == null) return;
        Vector3 p = owner.transform.position;
        if (!TerrainSpawns.TryGetGroundPos(p.x, p.z, out Vector3 pos)) return;
        SpawnEntity(type, level, pos, camp);
    }

    // 中立雕像（天使/恶魔）场上最多 1 个：已有任意中立雕像就不再刷
    private void GmSpawnNeutralStatue(EntityType type)
    {
        if (!BattleStarted) return;
        foreach (var e in EntityContainer.Entities)
            if (e != null && e.Alive && e.type.category == EntityCategory.NeutralStatue) return;
        if (TerrainSpawns.NeutralStatues.Count == 0) return;
        SpawnEntity(type, 1, TerrainSpawns.RandomOf(TerrainSpawns.NeutralStatues), EntityCamp.Neutral);
    }

    // 防御塔（水晶雕像）落在塔位锚点上，塔位与 CrystalStatue 的下标一一对应；朝向与开局生成一致，取最近守护点
    private void GmSpawnTowers(int count)
    {
        if (!BattleStarted) return;
        var towers = TerrainSpawns.Towers;
        if (towers.Count == 0) return;
        for (int k = 0; k < count; k++)
        {
            int index = Random.Range(0, towers.Count);
            ushort id = SpawnEntity(EntityType.CrystalStatue(index), 1, towers[index], EntityCamp.Defense);
            FacePoint(id, NearestGuard(towers[index]));
        }
    }

    // 击退：以 GM 自身为原点把 5 米内的实体向外推；只被护盾挡住，飞行与无刚体单位推不动
    private readonly List<EntityData> gmTargetScratch = new();

    private void GmKick(int clientId, float force)
    {
        if (!BattleStarted || force == 0f) return;
        var source = GetGmPlayerEntity(clientId);
        if (source == null) return;

        gmTargetScratch.Clear();
        Vector3 origin = source.transform.position;
        foreach (var e in EntityContainer.Entities)
        {
            if (e == null || !e.Alive || e.id == source.id) continue;
            if ((e.transform.position - origin).sqrMagnitude > gmKickRadius * gmKickRadius) continue;
            gmTargetScratch.Add(e);
        }

        foreach (var e in gmTargetScratch) e.ApplyKnockback(origin, force);
    }

    private static int GmCountAliveEntities()
    {
        int count = 0;
        foreach (var e in EntityContainer.Entities)
            if (e != null && e.Alive) count++;
        return count;
    }
    #endregion
}
