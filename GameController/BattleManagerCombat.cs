using System.Collections.Generic;
using Ros.Transport;
using TerrainGen;
using UnityEngine;

using Ros.Config;
public partial class BattleManager
{
    #region 服务器权威移动（双手键盘：角色相对移动 + 渐转，朝向服务器权威）
    private void TickMovement()
    {
        float dt = Time.deltaTime;
        foreach (var e in EntityContainer.Entities)
        {
            if (e == null || !e.Alive) continue;
            if (e.anim == null) continue; // 无动画单位不移动（与 EntityData.SetupBody 同一判据）
            e.TickVelocity(dt);
        }
    }
    #endregion

    #region 子弹容器（时间戳推进：位置 = 轨迹 Lerp(经过时长/生命)，不做增量移动）
    private const float AllTimeWindow = 999f; // hit.end = 0 表示全程都在伤害窗口内
    private readonly List<Bullet> activeBullets = new();
    private static readonly EntityData[] s_bulletBuffer = new EntityData[50];
    private static readonly HashSet<string> s_noHitReported = new(); // 缺命中球只报一次，避免每发刷屏

    private void AddBullet(AttackData attack, BulletTrajectory trajectory, float lifeTime, BulletHitSpec spec)
    {
        if (attack == null || trajectory == null) return;
        float now = Time.time;
        if (spec.Hits.Length == 0 && s_noHitReported.Add(spec.label ?? "未知特效"))
            Debug.LogError($"[子弹] {spec.label} 没有命中球（BulletVfxInfo.hits 为空）：这发子弹不会造成任何伤害，请补齐特效资产");

        activeBullets.Add(new Bullet()
        {
            attack = attack,
            trajectory = trajectory,
            lifeTime = ResolveBulletLife(spec, lifeTime, trajectory),
            spawnTime = now,
            lastTickTime = now,
            spec = spec,
            lastHitTime = new Dictionary<uint, float>(),
        });
    }

    // 寿命口径与客户端一致：资产填了存活时长就用资产的；<=0（-1 = 资产自身不清除）用技能给的，再退回轨迹时长
    private static float ResolveBulletLife(BulletHitSpec spec, float lifeTime, BulletTrajectory trajectory)
    {
        if (spec.lifeTime > 0f) return spec.lifeTime;
        return lifeTime > 0f ? lifeTime : trajectory.Duration;
    }

    private void TickBullets()
    {
        float now = Time.time;
        for (int i = activeBullets.Count - 1; i >= 0; i--)
        {
            var b = activeBullets[i];
            float age = b.Age(now);
            if (age >= b.lifeTime)
            {
                activeBullets.RemoveAt(i);
                continue;
            }

            // 只用到「时刻 → 轨迹位置」，不累计 deltaTime
            float prevAge = b.Age(b.lastTickTime);
            Vector3 pos = b.At(age);
            Vector3 prevPos = b.At(prevAge);
            Quaternion rot = FacingAt(b.trajectory, age, b.lifeTime);
            Quaternion prevRot = FacingAt(b.trajectory, prevAge, b.lifeTime);

            b.lastTickTime = now;
            activeBullets[i] = b; // 结构体写回采样时刻

            TryHitBullet(b, now, age, pos, prevPos, rot, prevRot);
        }
    }

    // 与客户端 BulletPlayer.Tangent 同一算法：取轨迹上稍后的一点定朝向；贴武器/贴骨骼的轨迹自带朝向
    private static Quaternion FacingAt(BulletTrajectory trajectory, float age, float lifeTime)
    {
        if (trajectory.TryGetRotation(out var fixedRotation)) return fixedRotation;
        float inv = lifeTime > 0f ? 1f / lifeTime : 0f;
        Vector3 cur = trajectory.Lerp(Mathf.Clamp01(age * inv));
        Vector3 dir = trajectory.Lerp(Mathf.Clamp01((age + 0.02f) * inv)) - cur;
        if (dir.sqrMagnitude <= 1e-6f) dir = trajectory.End - cur; // 静止时朝终点，与客户端同一口径
        return dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : Quaternion.identity;
    }

    private void TryHitBullet(Bullet b, float now, float age, Vector3 pos, Vector3 prevPos, Quaternion rot, Quaternion prevRot)
    {
        var attack = b.attack;
        var spec = b.spec;
        var hits = spec.Hits;
        for (int h = 0; h < hits.Length; h++)
        {
            var hit = hits[h];
            float end = hit.end <= 0f ? AllTimeWindow : hit.end;
            if (age < hit.start || age > end) continue; // 时间戳直接比较：窗口外不判定

            Vector3 offset = hit.center * spec.scale;
            Vector3 center = pos + rot * offset;
            Vector3 prevCenter = prevPos + prevRot * offset;
            float radius = hit.radius * spec.scale;
#if UNITY_EDITOR
            DamageRangeDebugHost.Capsule(prevCenter, center, radius); // 调试：可视化本段命中球扫掠范围
#endif
            int count = EntityPhysics.OverlapCapsuleXZ(prevCenter, center, radius, s_bulletBuffer);
            for (int i = 0; i < count; i++)
            {
                var e = s_bulletBuffer[i];
                if (e == null || !e.Alive) continue;
                if (attack.shooterCategory is not { } shooterCategory) continue; // 无施法者：不结算敌对判定
                if (e.id == attack.shooter || !EntityCampUtil.IsHostile(shooterCategory, e.camp)) continue;
                // 同一目标按伤害间隔冷却重复结算：冷却按命中球段各自记录，段与段互不影响；间隔 <=0 时一段只结算一次
                uint hitKey = ((uint)h << 16) | e.id;
                if (b.lastHitTime.TryGetValue(hitKey, out float lastHit) &&
                    (spec.interval <= 0f || now - lastHit < spec.interval)) continue;
                b.lastHitTime[hitKey] = now;

                // 命中点取上一帧的命中球心：高速弹一帧穿过目标时用当前位置定方向会反转（也是无攻击者时的击退来源）
                Vector3 floatPos = new Vector3(e.transform.position.x, prevCenter.y, e.transform.position.z);
                if (e.TryBlockByHitProtection(floatPos)) continue; // 连击保护：只飘字，不结算伤害/buff/击退/受击

                int damage = attack.GetDamage(e, out CombatLabelType type, spec.rates != null ? spec.rates[h] : 1f);
                // 闪避 / 失效：命中附带的 Buff 不施加，onHit 与击退等其余表现照旧
                bool hitLanded = e.ProcessHit(attack, damage, type, prevCenter, floatPos);
                if (hitLanded && attack.addEffectEvent != null && e.effectController != null)
                {
                    attack.addEffectEvent.Invoke(e.effectController);
                }
                if (attack.onHit != null) attack.onHit.Invoke(e);
            }
        }
    }
    #endregion

    #region 打击震颤
    // 只有命中类参与（DoT / 反伤 / 闪避 / 失效不发）；攻击方与受击方是玩家才各发一条
    public void SendHitFeel(EntityData attacker, EntityData target, CombatLabelType type)
    {
        if (type != CombatLabelType.Normal && type != CombatLabelType.Crit && type != CombatLabelType.Shield) return;

        if (attacker != null && EntityOwnerClient.TryGetValue(attacker.id, out int attackerClient))
        {
            Tool.NetworkManager.SendHitFeel(attackerClient,
                new SCHitFeel { role = SCHitFeel.RoleAttacker, type = (int)type });
        }
        if (EntityOwnerClient.TryGetValue(target.id, out int targetClient))
        {
            Tool.NetworkManager.SendHitFeel(targetClient,
                new SCHitFeel { role = SCHitFeel.RoleVictim, type = (int)type });
        }
    }
    #endregion

    #region 死亡 / 复活 / 愈战愈勇
    private class ReviveState
    {
        public int deathCount;
        public float progress;
        public List<int> slotSkills; // 死亡瞬间的技能栏选择，复活重建实体后沿用
    }
    private readonly Dictionary<int, ReviveState> reviveStates = new();

    public readonly Dictionary<int, int> KillCountByClient = new();

    private void HandleDeath(EntityData entity)
    {
        // 掉落必须在实体销毁前结算：经验与卡牌机会按 entity.csv 的掉落列发给归属方
        GrantKillRewards(entity);

        // 击杀归属：击杀者由 lastAttacker 反查（归属已随死亡移除时算不出，例如同归于尽 → 不计）
        var killer = entity.lastAttacker;
        bool victimIsPlayer = entity.type.category == EntityCategory.Character_Attack ||
                              entity.type.category == EntityCategory.Character_Defense;
        if (victimIsPlayer && killer != null && EntityCampUtil.IsHostile(killer.type.category, entity.camp) &&
            EntityOwnerClient.TryGetValue(killer.id, out var killerClient))
        {
            KillCountByClient.TryGetValue(killerClient, out int killCount);
            KillCountByClient[killerClient] = killCount + 1;
        }

        // 玩家死亡才进事件列表：有归属击杀 = 玩家间击败，无归属 = 无源死亡
        if (victimIsPlayer && EntityOwnerClient.TryGetValue(entity.id, out var victimOwner))
        {
            var e = killer != null && EntityOwnerClient.TryGetValue(killer.id, out var killerOwner)
                ? new SCBattleEvent()
                {
                    type = SCBattleEvent.Type.PlayerKill,
                    textId = SCBattleEvent.PlayerNameBase + killerOwner,
                    textId2 = SCBattleEvent.PlayerNameBase + victimOwner,
                }
                : new SCBattleEvent()
                {
                    type = SCBattleEvent.Type.DeathUnattributed,
                    textId = SCBattleEvent.PlayerNameBase + victimOwner,
                };
            Tool.NetworkManager.SendBattleEvent(e);
        }

        if (EntityOwnerClient.TryGetValue(entity.id, out var owner))
        {
            EntityOwnerClient.Remove(entity.id);
            PlayerEntityId.Remove(owner); // 复活时重建并重新绑定
            // 归属已在这里摘除，DestroyEntity 里再查就查不到：死亡必须在此把 -1 发出去
            Tool.NetworkManager.SendPlayerEntity(owner, -1);

            if (!reviveStates.TryGetValue(owner, out var rs))
            {
                rs = new ReviveState();
                reviveStates[owner] = rs;
            }
            rs.deathCount++;
            rs.progress = 0f;
            if (entity.skillController != null) rs.slotSkills = entity.skillController.CaptureSlots();
            SendReviveState(owner, rs, 0, ready: false); // 只报死亡次数，进度由客户端按配表本地推演
        }

        // 死亡即销毁：不再播死亡动画，也就不存在"等动画事件才销毁"的挂起尸体
        bool wasAttack = entity.type.category == EntityCategory.Character_Attack;
        DestroyEntity(entity.id);

        // 雕像的分层减伤重算在其子类 OnKilled 里处理（该方法先于本方法调用）
        if (wasAttack)
        {
            CheckAttackWiped(); // 进攻方全灭 → 立即按主守护点血量结算（策划案 17.2，不直接判负）
        }
    }

    private void CheckAttackWiped()
    {
        foreach (var e in EntityContainer.Entities)
        {
            if (e != null && e.Alive && e.type.category == EntityCategory.Character_Attack) return;
        }
        EndBattle(WinnerByMainStatue());
    }

    // 珊瑚被摧毁：按尺寸给击杀者回血
    public void OnCoralDestroyed(EntityData coral)
    {
        HealCoralKiller(coral);
    }

    // 击败珊瑚的玩家角色实体（含 AI 代打）；击杀者未归属任何客户端时返回 null
    private EntityData GetCoralKillerPlayer(EntityData coral, out int clientId)
    {
        clientId = -1;
        var killer = coral.lastAttacker;
        if (killer == null) return null;
        if (!EntityOwnerClient.TryGetValue(killer.id, out clientId)) return null;
        if (!PlayerEntityId.TryGetValue(clientId, out var playerId)) return null;
        return EntityContainer.Entities.TryGetObject(playerId, out var player) ? player : null;
    }

    // 小/大珊瑚各配一个固定恢复量（global_variables），夹到生命上限
    private void HealCoralKiller(EntityData coral)
    {
        var player = GetCoralKillerPlayer(coral, out _);
        if (player == null || !player.Alive) return;
        float heal = coral.type.value <= 0
            ? GlobalVariable.F(global_variablesEnum.coral_small_regeneration)
            : GlobalVariable.F(global_variablesEnum.coral_large_regeneration);
        if (heal <= 0f) return;
        player.attributes.Heal(heal);
        player.healthDirty = true;
    }

    private void TickRevive()
    {
        float dt = UnityEngine.Time.deltaTime;
        foreach (var pair in reviveStates)
        {
            int clientId = pair.Key;
            if (PlayerEntityId.ContainsKey(clientId)) continue; // 已复活

            var rs = pair.Value;
            if (!PlayerCamps.TryGetValue(clientId, out var camp)) continue;

            // 复活时长固定看配表（第几次复活 → 秒数），不再区分昼夜
            var revive = ReviveTimeConfig.GetClamped(Mathf.Max(1, rs.deathCount));
            if (camp == PlayerCampType.Attack)
            {
                rs.progress += dt / revive.attackReviveSeconds;
            }
            else
            {
                rs.progress += dt / revive.defenseReviveSeconds;
            }

            if (rs.progress >= 1f) RespawnPlayer(clientId, rs);
        }
    }

    private void RespawnPlayer(int clientId, ReviveState rs)
    {
        if (!PlayerCamps.TryGetValue(clientId, out var camp)) return;
        if (!PlayerInfoList.TryGetValue(clientId, out var info)) return;
        bool isAttack = camp == PlayerCampType.Attack;
        EntityType characterType = isAttack ? info.attackCharacter : info.defenseCharacter;
        int level = GetPlayerLevel(clientId); // 复活沿用局内等级，压制关系不因死亡丢失

        // 复活点与出生一致：进攻方回地图外围随机点，防守方落在主守护点附近
        Vector3 pos = isAttack ? GetAttackSpawnPos() : GetDefenseSpawnPos();

        EntityCamp entityCamp = isAttack ? EntityCamp.Attack : EntityCamp.Defense;
        ushort entityId = SpawnEntity(characterType, level, pos, entityCamp);
        if (entityId == 0)
        {
            rs.progress = 1f; // 模板缺失：保持攒满状态稍后重试
            return;
        }
        PlayerEntityId[clientId] = entityId;
        EntityOwnerClient[entityId] = clientId;
        Tool.NetworkManager.SendPlayerEntity(clientId, entityId);
        ExitSpectate(clientId); // 复活即结束观战，视野回到自己实体

        var data = GetEntity(entityId);
        EnableSkillBar(clientId, data); // 先启用栏位，复活才能沿用死亡前的槽位选择
        if (data != null && data.skillController != null)
        {
            // 技能不随死亡丢失：持有表取玩家级（死亡期间抽到的也算），槽位沿用死亡前的选择
            data.skillController.ApplyLoadout(PlayerSkillList(clientId), rs.slotSkills);
        }
        if (data is PlayerEntityData player)
        {
            player.robotControlled = RobotClients.Contains(clientId);
            player.aiControlled = AiClients.Contains(clientId); // 复活换实体：接管状态按会话恢复
            if (player.aiControlled && player.AiRelay == null)
                player.AiRelay = new AiIntentRelay(player, clientId); // 新实体同样默认挂上行为状态机
        }
        SendSkillRecharge(clientId); // 新实体满库存：把重生后的技能栏快照同步给本人

        // 愈战愈勇：第 n 条命层数（配表按死亡次数给出，永久 Buff）
        int fury = BattleFuryLayerConfig.GetClamped(rs.deathCount).buffLayerCount;
        if (fury > 0 && data != null)
        {
            if (data.effectController != null) data.effectController.AddEffect(EffectType.BattleFury, fury, float.MaxValue);
        }

        rs.progress = 0f; // deathCount 保留，供下次倍率与叠层
        // 复活换了新实体 id：客户端由 SCPlayerEntity 更新自己的实体 id 与名牌归属，不再单独下发会话信息
        SendReviveState(clientId, rs, entityId, ready: true);
    }

    // 只在死亡、复活、重连补发这三个时机下发；进度本身由客户端读配表本地推演
    private void SendReviveState(int clientId, ReviveState rs, ushort entityId, bool ready)
    {
        Tool.NetworkManager.SendReviveInfo(clientId, new SCReviveInfo()
        {
            deathCount = rs.deathCount,
            progress = Mathf.Clamp01(rs.progress),
            ready = ready,
            entityId = entityId,
            furyStack = BattleFuryLayerConfig.GetClamped(rs.deathCount).buffLayerCount,
        });
    }

    private void ClearBattleState()
    {
        activeBullets.Clear();
        reviveStates.Clear();
        neutralStatueRespawnTime = -1f; // 由 SpawnBattleWorld 重新排首次刷新
        guardPointHealthScale = 0f;     // 同上：由 SpawnBattleWorld 重新取人数倍率
        HarvestByClient.Clear();
        KillCountByClient.Clear();
        minimapRadiusByClient.Clear(); // 每局雷达显示半径回到默认档
        bigMapOpenByClient.Clear(); // 每局大地图展开状态重置
        visionStates.Clear(); // 每局视野锚点与观战状态重置
        skillFailPromptTime.Clear();
        lastAttackerTotal = lastDefenseTotal = lastAttackerAlive = lastDefenseAlive = -1; // 每局重置，保证开局首帧必发
        for (int i = 0; i < defensePointHealth.Length; i++) defensePointHealth[i] = -1; // 守护点未生成前血量未知
        for (int i = 0; i < sentDefensePointHealth.Length; i++) sentDefensePointHealth[i] = -1; // 同上：开局必发一次
        plagueGuardReadyTime.Clear();
    }

    // 重连补发：把「已发送快照」作废，下一帧的变更检测会自然重播一次（值不变，对其它客户端无害）
    private void InvalidateBroadcastSnapshots()
    {
        lastAttackerTotal = lastDefenseTotal = lastAttackerAlive = lastDefenseAlive = lastSpectatorCount = -1;
        for (int i = 0; i < sentDefensePointHealth.Length; i++) sentDefensePointHealth[i] = -1;
    }

    #region 守护点当前血量（与视野解耦，变更即广播；名字由客户端读 entity.csv，上限 = 配表生命 × 随包下发的人数倍率）
    private readonly int[] defensePointHealth = { -1, -1, -1, -1 }; // 0 主点 / 1~3 副点，-1 = 未生成
    private readonly int[] sentDefensePointHealth = { -1, -1, -1, -1 };

    private void TickDefensePointProgress()
    {
        foreach (var statue in EntityContainer.GuardStatues)
        {
            if (statue == null) continue;
            int index = DefensePointIndexOf(statue);
            if (index < 0) continue;
            defensePointHealth[index] = HealthOf(statue);
        }

        bool changed = false;
        for (int i = 0; i < defensePointHealth.Length; i++)
        {
            if (defensePointHealth[i] == sentDefensePointHealth[i]) continue;
            sentDefensePointHealth[i] = defensePointHealth[i];
            changed = true;
        }
        if (!changed) return;

        Tool.NetworkManager.SendDefensePointProgress(new SCDefensePointProgress()
        {
            mainHealth = defensePointHealth[0],
            sub1Health = defensePointHealth[1],
            sub2Health = defensePointHealth[2],
            sub3Health = defensePointHealth[3],
            healthScale = guardPointHealthScale,
        });
    }

    // 实体销毁后算不出血量，血量在这里定死 0
    private void MarkDefensePointDestroyed(EntityData statue)
    {
        int index = DefensePointIndexOf(statue);
        if (index >= 0) defensePointHealth[index] = 0;
    }

    private static int DefensePointIndexOf(EntityData statue)
    {
        if (statue.type == EntityType.KingStatue) return 0;
        if (statue.type.category != EntityCategory.GuardStatue) return -1;
        return statue.type.value >= 0 && statue.type.value < GlobalConfig.sage_statue_count ? statue.type.value + 1 : -1;
    }

    // 血量向下取整，客户端显示与进度条用同一个数
    private static int HealthOf(EntityData statue) => Mathf.Max(0, Mathf.FloorToInt(statue.attributes.Health));
    #endregion

    #region 技能释放回执
    private const float SkillFailPromptInterval = 1f; // 同一客户端提示间隔，连点不刷屏
    private readonly Dictionary<int, float> skillFailPromptTime = new();

    // 技能失败回包：玩家看得懂的原因都提示（沉默/强控/冷却中），冷却同时回推快照纠正客户端显示
    public void NotifySkillUseFailed(EntityData entity, EntitySkillController.UseResult result)
    {
        if (entity == null) return;
        if (result == EntitySkillController.UseResult.Cooling)
        {
            if (entity.skillController == null) return;
            if (!EntityOwnerClient.TryGetValue(entity.id, out var coolingClientId)) return;
            if (Tool.NetworkManager != null)
                Tool.NetworkManager.SendSkillRecharge(coolingClientId, entity.skillController.BuildRecharge());
            SendFailPrompt(coolingClientId, promptEnum.skill_cooling);
            return;
        }

        promptEnum messageId;
        if (result == EntitySkillController.UseResult.Silenced) messageId = promptEnum.silenced;
        else if (result == EntitySkillController.UseResult.Controlled) messageId = promptEnum.controlled;
        else return;

        if (!EntityOwnerClient.TryGetValue(entity.id, out var clientId)) return; // AI / 非玩家没有客户端
        SendFailPrompt(clientId, messageId);
    }

    private void SendFailPrompt(int clientId, promptEnum messageId)
    {
        if (skillFailPromptTime.TryGetValue(clientId, out var last) && Time.time - last < SkillFailPromptInterval) return;
        skillFailPromptTime[clientId] = Time.time;
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendPrompt(clientId, (int)messageId);
    }

    // 受理成功：服务器已扣过库存，把新快照推给施法者本人（AI / 无主实体没有客户端，直接跳过）
    public void NotifySkillUsed(EntityData entity, int skillId)
    {
        if (entity == null || skillId < 0) return;
        if (entity.skillController == null) return;
        if (!EntityOwnerClient.TryGetValue(entity.id, out var clientId)) return;
        if (Tool.NetworkManager != null)
            Tool.NetworkManager.SendSkillRecharge(clientId, entity.skillController.BuildRecharge());
    }
    #endregion

    private int lastAttackerTotal = -1, lastDefenseTotal = -1, lastAttackerAlive = -1, lastDefenseAlive = -1, lastSpectatorCount = -1;

    // 双方玩家（真人+AI）总人数、存活数与观战人数：ManagedUpdate 每帧比对，变更才下发（阵亡/复活/进出房/GM 击杀统一在此收口）
    private void TickCampAliveCount()
    {
        int aTotal = 0, dTotal = 0, aAlive = 0, dAlive = 0, sCount = 0;
        foreach (var pair in PlayerCamps)
        {
            if (pair.Value == PlayerCampType.Spectator)
            {
                sCount++; // 观战方不计入双方人数，单独统计
                continue;
            }
            bool attack = pair.Value == PlayerCampType.Attack;
            if (attack) aTotal++; else dTotal++;
            var data = PlayerEntityId.TryGetValue(pair.Key, out var eid) ? GetEntity(eid) : null;
            if (data != null && data.Alive)
            {
                if (attack) aAlive++; else dAlive++;
            }
        }
        if (aTotal == lastAttackerTotal && dTotal == lastDefenseTotal
            && aAlive == lastAttackerAlive && dAlive == lastDefenseAlive
            && sCount == lastSpectatorCount) return;
        lastAttackerTotal = aTotal;
        lastDefenseTotal = dTotal;
        lastAttackerAlive = aAlive;
        lastDefenseAlive = dAlive;
        lastSpectatorCount = sCount;
        Tool.NetworkManager.SendCampAliveCount(new SCCampAliveCount()
        {
            attackerTotal = aTotal,
            defenseTotal = dTotal,
            attackerAlive = aAlive,
            defenseAlive = dAlive,
            spectatorCount = sCount,
        });
    }
    #endregion
}
