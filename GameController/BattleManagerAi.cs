using System.Collections.Generic;
using System.IO;
using System.Text;
using Ros.Config;
using Ros.Transport;
using TerrainGen;
using UnityEngine;
using UnityEngine.AI;

// 战斗阶段的 AI 命令与感知缓存：开战挂上命令、结束摘掉，意图上行与查询都在这里
public partial class BattleManager
{
    private const string ScreenshotFolder = "Screenshots";
    private const float CameraRestoreDelay = 0.2f;
    private const int ImportantReadCount = 20;

    private readonly Dictionary<ushort, SCMinimapEntity> aiMinimap = new();
    private readonly Dictionary<ushort, float> aiMinimapSeen = new();
    private readonly List<ushort> aiMinimapExpired = new();
    private readonly List<string> aiSelfBuffs = new();

    private int aiSelfHealth;
    private int aiSelfMaxHealth;
    private SCDefensePointProgress aiGuardProgress;
    private bool aiHasGuardProgress; // 缓存是结构体，靠它区分「从没收到过」
    private bool aiCommandsBound;    // OnBattleEnd 与 OnExitWorld 都会来，靠它保证只摘一次

    #region 感知缓存（供命令行与 Lua 查询）
    public int AiSelfHealth => aiSelfHealth;

    public int AiSelfMaxHealth => aiSelfMaxHealth;

    public IReadOnlyList<string> AiSelfBuffs => aiSelfBuffs;

    public SCDefensePointProgress AiGuardSnapshot => aiGuardProgress;

    public bool AiHasGuardSnapshot => aiHasGuardProgress;

    public void AiCopyMinimap(List<SCMinimapEntity> buffer)
    {
        buffer.Clear();
        foreach (var pair in aiMinimap) buffer.Add(pair.Value);
    }

    public bool AiTryGetSelfPos(out Vector3 pos)
    {
        pos = default;
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession) return false;
        if (Tool.ClientLogicManager == null) return false;
        return Tool.ClientLogicManager.EntityPlayers.TryGetEntityPosition(loading.OwnEntityId, out pos);
    }

    public float AiSelfYaw()
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession) return 0f;
        if (Tool.ClientLogicManager == null) return 0f;
        var view = Tool.ClientLogicManager.EntityPlayers.GetView(loading.OwnEntityId);
        return view != null ? view.transform.eulerAngles.y : 0f;
    }
    #endregion

    #region 阶段订阅
    // Awake 里挂一次：开战/结束决定命令注册，其余是感知缓存的数据源
    private void SubscribeAiStage()
    {
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnAiStageStart);
        EventManager.AddEvent<int>(ClientEvent.OnBattleEnd, OnAiStageEnd);
        EventManager.AddEvent(ClientEvent.OnExitWorld, OnAiExitWorld);
        EventManager.AddEvent(ClientEvent.OnRestartGame, OnAiExitWorld); // 掉线也走这条，战斗命令一并摘掉
        EventManager.AddEvent<SCMinimapEntity>(ClientEvent.OnMinimapUpdate, OnAiMinimapUpdate);
        EventManager.AddEvent<SCEntityDisplayInfo>(ClientEvent.OnEntityDisplayUpdate, OnAiEntityDisplay);
        EventManager.AddEvent<SCDefensePointProgress>(ClientEvent.OnDefensePointProgress, OnAiGuardUpdate);
    }

    private void UnsubscribeAiStage()
    {
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnAiStageStart);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleEnd, OnAiStageEnd);
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, OnAiExitWorld);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, OnAiExitWorld);
        EventManager.RemoveEvent<SCMinimapEntity>(ClientEvent.OnMinimapUpdate, OnAiMinimapUpdate);
        EventManager.RemoveEvent<SCEntityDisplayInfo>(ClientEvent.OnEntityDisplayUpdate, OnAiEntityDisplay);
        EventManager.RemoveEvent<SCDefensePointProgress>(ClientEvent.OnDefensePointProgress, OnAiGuardUpdate);
    }

    private void OnAiStageStart(int camp)
    {
        if (AtServer) return;
        BindAiCommands();
    }

    private void OnAiStageEnd(int gameState)
    {
        if (AtServer) return;
        UnbindAiCommands();
    }

    private void OnAiExitWorld()
    {
        if (AtServer) return;
        UnbindAiCommands();
    }

    private void OnDestroy()
    {
        UnbindAiCommands();
        UnsubscribeAiStage();
    }

    private void BindAiCommands()
    {
        if (aiCommandsBound) return;
        aiCommandsBound = true;

        AiCommandBus.Bind(ai_commandEnum.stateMachine_stop, OnAiStop);
        AiCommandBus.Bind(ai_commandEnum.stateMachine_moveTo, OnAiMoveTo);
        AiCommandBus.Bind(ai_commandEnum.stateMachine_attack, OnAiAttack);
        AiCommandBus.Bind(ai_commandEnum.stateMachine_collectCoral, OnAiCollectCoral);
        AiCommandBus.Bind(ai_commandEnum.stateMachine_castSkill, OnAiCastSkill);
        AiCommandBus.Bind(ai_commandEnum.luaStateMachine_start, OnAiStartLua);
        AiCommandBus.Bind(ai_commandEnum.luaStateMachine_stop, OnAiStopLua);
        AiCommandBus.Bind(ai_commandEnum.terrain_points, OnAiPoints);
        AiCommandBus.Bind(ai_commandEnum.terrain_path, OnAiPath);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_all, OnAiGetInfo);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_player, OnAiPlayer);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_reviveProgress, OnAiReviveProgress);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_pos, OnAiPos);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_importantEvent, OnAiImportantEvent);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_guardProgress, OnAiGuardProgress);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_minimapEntities, OnAiMinimapEntities);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_dayNightInfo, OnAiDayNightInfo);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_camp, OnAiCampCount);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_cardCount, OnAiCardCount);
        AiCommandBus.Bind(ai_commandEnum.camera_setActive, OnAiSetCameraRender);
        AiCommandBus.Bind(ai_commandEnum.camera_screenShot, OnAiScreenshot);

        AiCommandBus.BindIntent(AiIntent.Stand, OnAiIntentStand);
        AiCommandBus.BindIntent(AiIntent.MoveTo, OnAiIntentMove);
        AiCommandBus.BindIntent(AiIntent.AttackTarget, OnAiIntentAttack);
        AiCommandBus.BindIntent(AiIntent.Stop, OnAiIntentStop);
    }

    private void UnbindAiCommands()
    {
        if (!aiCommandsBound) return;
        aiCommandsBound = false;

        AiCommandBus.Unbind(ai_commandEnum.stateMachine_stop, OnAiStop);
        AiCommandBus.Unbind(ai_commandEnum.stateMachine_moveTo, OnAiMoveTo);
        AiCommandBus.Unbind(ai_commandEnum.stateMachine_attack, OnAiAttack);
        AiCommandBus.Unbind(ai_commandEnum.stateMachine_collectCoral, OnAiCollectCoral);
        AiCommandBus.Unbind(ai_commandEnum.stateMachine_castSkill, OnAiCastSkill);
        AiCommandBus.Unbind(ai_commandEnum.luaStateMachine_start, OnAiStartLua);
        AiCommandBus.Unbind(ai_commandEnum.luaStateMachine_stop, OnAiStopLua);
        AiCommandBus.Unbind(ai_commandEnum.terrain_points, OnAiPoints);
        AiCommandBus.Unbind(ai_commandEnum.terrain_path, OnAiPath);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_all, OnAiGetInfo);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_player, OnAiPlayer);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_reviveProgress, OnAiReviveProgress);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_pos, OnAiPos);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_importantEvent, OnAiImportantEvent);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_guardProgress, OnAiGuardProgress);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_minimapEntities, OnAiMinimapEntities);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_dayNightInfo, OnAiDayNightInfo);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_camp, OnAiCampCount);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_cardCount, OnAiCardCount);
        AiCommandBus.Unbind(ai_commandEnum.camera_setActive, OnAiSetCameraRender);
        AiCommandBus.Unbind(ai_commandEnum.camera_screenShot, OnAiScreenshot);

        AiCommandBus.UnbindIntent(AiIntent.Stand, OnAiIntentStand);
        AiCommandBus.UnbindIntent(AiIntent.MoveTo, OnAiIntentMove);
        AiCommandBus.UnbindIntent(AiIntent.AttackTarget, OnAiIntentAttack);
        AiCommandBus.UnbindIntent(AiIntent.Stop, OnAiIntentStop);
    }

    private void OnAiMinimapUpdate(SCMinimapEntity entity)
    {
        aiMinimap[entity.entityId] = entity;
        aiMinimapSeen[entity.entityId] = Time.time;
    }

    private void OnAiEntityDisplay(SCEntityDisplayInfo info)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || info.entityId != loading.OwnEntityId) return;

        if (info.hasHealth)
        {
            aiSelfHealth = info.health;
            aiSelfMaxHealth = info.maxHealth;
        }
        if (!info.hasBuffs) return;

        aiSelfBuffs.Clear();
        for (int i = 0; i < info.buffs.Count; i++)
        {
            aiSelfBuffs.Add(info.buffs[i].type + ":" + info.buffs[i].level);
        }
    }

    private void OnAiGuardUpdate(SCDefensePointProgress info)
    {
        aiGuardProgress = info;
        aiHasGuardProgress = true;
    }

    // 小地图缓存按 minimap_entity_timeout 剔除，ManagedUpdate 每帧推一次
    private void AiPerceptionTick()
    {
        if (aiMinimapSeen.Count == 0) return;

        aiMinimapExpired.Clear();
        foreach (var pair in aiMinimapSeen)
        {
            if (Time.time - pair.Value <= GlobalConfig.minimap_entity_timeout) continue;
            aiMinimapExpired.Add(pair.Key);
        }
        for (int i = 0; i < aiMinimapExpired.Count; i++)
        {
            ushort id = aiMinimapExpired[i];
            aiMinimapSeen.Remove(id);
            aiMinimap.Remove(id);
        }
    }
    #endregion

    #region 意图上行
    private void OnAiIntentStand(AiIntentRequest request) => SendAiIntent(AiIntent.Stand, Vector2.zero, 0, request);

    private void OnAiIntentStop(AiIntentRequest request) => SendAiIntent(AiIntent.Stop, Vector2.zero, 0, request);

    private void OnAiIntentMove(AiIntentRequest request) => SendAiIntent(AiIntent.MoveTo, request.Point, 0, request);

    private void OnAiIntentAttack(AiIntentRequest request) => SendAiIntent(AiIntent.AttackTarget, Vector2.zero, request.TargetId, request);

    private void SendAiIntent(AiIntent intent, Vector2 position, ushort targetId, AiIntentRequest request)
    {
        if (!NetworkManager.CanSendWorldCommand)
        {
            request.Reject("未联网");
            return;
        }
        Tool.NetworkManager.SendAiIntent(new CSAiIntent
        {
            intent = (int)intent,
            posX = position.x,
            posZ = position.y,
            targetId = targetId,
        });
    }

    // 命令行的动作命令都标了「无返回值」：成功不出声，失败才回一句错误
    private bool SendAiIntent(AiIntent intent, Vector2 position, ushort targetId, AiRequest request)
    {
        if (!NetworkManager.CanSendWorldCommand)
        {
            request.Reject("未联网");
            return false;
        }
        Tool.NetworkManager.SendAiIntent(new CSAiIntent
        {
            intent = (int)intent,
            posX = position.x,
            posZ = position.y,
            targetId = targetId,
        });
        return true;
    }
    #endregion

    #region 状态机动作
    private void OnAiStop(AiRequest request) => SendAiIntent(AiIntent.Stop, Vector2.zero, 0, request);

    private void OnAiMoveTo(AiRequest request) =>
        SendAiIntent(AiIntent.MoveTo, new Vector2(request.Float(0), request.Float(1)), 0, request);

    // 附近敌人 = 小地图上非中立、非我方的单位（珊瑚单独走 collectCoral）
    private void OnAiAttack(AiRequest request)
    {
        if (!TryAiFindNearestEnemy(out ushort enemyId))
        {
            request.Reject("小地图上没有敌人");
            return;
        }
        SendAiIntent(AiIntent.AttackTarget, Vector2.zero, enemyId, request);
    }

    private void OnAiCollectCoral(AiRequest request)
    {
        if (!TryAiFindNearestCoral(out ushort coralId))
        {
            request.Reject("小地图上没有珊瑚");
            return;
        }
        SendAiIntent(AiIntent.AttackTarget, Vector2.zero, coralId, request);
    }

    private void OnAiCastSkill(AiRequest request)
    {
        int skillId = request.Int(0);
        if (!SkillManager.TryGet(skillId, out _))
        {
            request.Reject($"技能 {skillId} 未注册");
            return;
        }
        if (!NetworkManager.CanSendWorldCommand)
        {
            request.Reject("未联网");
            return;
        }
        Tool.NetworkManager.SendAiSkillCast(skillId);
    }

    private void OnAiStartLua(AiRequest request)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            request.Reject("Lua 接口未就绪");
            return;
        }
        request.Reply(lua.ActivateStateMachine());
    }

    private void OnAiStopLua(AiRequest request)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            request.Reject("Lua 接口未就绪");
            return;
        }
        request.Reply(lua.StopStateMachine());
    }

    private bool TryAiFindNearestCoral(out ushort coralId) => TryAiFindNearest(EntityCategory.Coral, out coralId);

    private bool TryAiFindNearestEnemy(out ushort enemyId)
    {
        enemyId = 0;
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession) return false;

        EntityCamp mine = loading.Camp;
        if (!AiTryGetSelfPos(out Vector3 self)) return false;

        float best = float.MaxValue;
        bool found = false;
        foreach (var pair in aiMinimap)
        {
            var entity = pair.Value;
            if (entity.camp == mine || entity.camp == EntityCamp.Neutral) continue;
            if (entity.camp == EntityCamp.None) continue;
            if (entity.type.category == EntityCategory.Coral) continue;

            float dx = entity.posX - self.x;
            float dz = entity.posZ - self.z;
            float sqr = dx * dx + dz * dz;
            if (sqr >= best) continue;
            best = sqr;
            enemyId = entity.entityId;
            found = true;
        }
        return found;
    }

    private bool TryAiFindNearest(EntityCategory category, out ushort entityId)
    {
        entityId = 0;
        if (!AiTryGetSelfPos(out Vector3 self)) return false;

        float best = float.MaxValue;
        bool found = false;
        foreach (var pair in aiMinimap)
        {
            var entity = pair.Value;
            if (entity.type.category != category) continue;
            float dx = entity.posX - self.x;
            float dz = entity.posZ - self.z;
            float sqr = dx * dx + dz * dz;
            if (sqr >= best) continue;
            best = sqr;
            entityId = entity.entityId;
            found = true;
        }
        return found;
    }
    #endregion

    #region 地形
    private void OnAiPath(AiRequest request)
    {
        float x = request.Float(0);
        float z = request.Float(1);
        if (!AiTryGetSelfPos(out Vector3 from))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }

        Vector3 to = new Vector3(x, from.y, z);
        if (!NavMesh.SamplePosition(from, out var startHit, 2f, NavMesh.AllAreas))
        {
            request.Reply("可达=否 原因=起点不在导航面上");
            return;
        }
        if (!NavMesh.SamplePosition(to, out var endHit, 2f, NavMesh.AllAreas))
        {
            request.Reply("可达=否 原因=终点不在导航面上");
            return;
        }

        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path);
        if (!ok || path.corners.Length < 2)
        {
            request.Reply("可达=否 原因=没有完整路径");
            return;
        }

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }
        request.Reply($"可达=是 长度={length:0.##} 拐点={path.corners.Length}");
    }

    // 守护点 / 防御塔 / 中立雕像 / 双方出生点
    private void OnAiPoints(AiRequest request)
    {
        if (!TerrainSpawns.Built)
        {
            request.Reject("地形点位未就绪");
            return;
        }

        var builder = new StringBuilder();
        AppendPoint(builder, "主守护点", TerrainSpawns.MainGuard);
        for (int i = 0; i < TerrainSpawns.SubGuards.Count; i++)
            AppendPoint(builder, "副守护点" + (i + 1), TerrainSpawns.SubGuards[i]);
        for (int i = 0; i < TerrainSpawns.Towers.Count; i++)
            AppendPoint(builder, "防御塔" + (i + 1), TerrainSpawns.Towers[i]);
        for (int i = 0; i < TerrainSpawns.NeutralStatues.Count; i++)
            AppendPoint(builder, "中立雕像" + (i + 1), TerrainSpawns.NeutralStatues[i]);
        AppendPoint(builder, "进攻出生", TerrainSpawns.AttackSpawn);
        AppendPoint(builder, "防守出生", TerrainSpawns.DefenseSpawn);
        request.Reply(builder.ToString());
    }

    private static void AppendPoint(StringBuilder builder, string label, Vector3 point)
    {
        if (builder.Length > 0) builder.Append('\n');
        builder.Append(label).Append("=(").Append(point.x.ToString("0.##")).Append(',').Append(point.z.ToString("0.##")).Append(')');
    }
    #endregion

    #region 战斗查询
    private void OnAiPos(AiRequest request)
    {
        if (!AiTryGetSelfPos(out Vector3 pos))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }
        request.Reply($"坐标=({pos.x:0.##},{pos.z:0.##})");
    }

    // 是否存活、等级、生命、经验条、buff、技能状态
    private void OnAiPlayer(AiRequest request)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession)
        {
            request.Reject("没有战斗会话");
            return;
        }
        if (!AiTryGetSelfPos(out Vector3 pos))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("实体=").Append(loading.OwnEntityId)
            .Append(" 阵营=").Append(loading.Camp)
            .Append(" 存活=").Append(aiSelfHealth > 0 ? "是" : "否")
            .Append(" 生命=").Append(aiSelfHealth).Append('/').Append(aiSelfMaxHealth);
        AppendLevel(builder);
        builder.Append(" 坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(AiSelfYaw().ToString("0.#"));

        builder.Append("\nbuff=");
        if (aiSelfBuffs.Count == 0) builder.Append("无");
        for (int i = 0; i < aiSelfBuffs.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(aiSelfBuffs[i]);
        }

        builder.Append("\n技能 ");
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null)
        {
            builder.Append("管理器未就绪");
        }
        else
        {
            var active = skills.ActiveSkills;
            for (int i = 0; i < active.Count; i++)
            {
                int skillId = active[i];
                builder.Append("\n槽位").Append(i)
                    .Append(": id=").Append(skillId)
                    .Append(" 进度=").Append(skills.Recharge.GetCycleProgress(skillId).ToString("0.##"))
                    .Append(" 库存=").Append(skills.Recharge.GetStore(skillId));
            }

            builder.Append("\n持有:");
            var held = skills.HeldSkills;
            if (held.Count == 0) builder.Append(" 无");
            for (int i = 0; i < held.Count; i++) builder.Append(' ').Append(held[i]);
        }

        request.Reply(builder.ToString());
    }

    // 复活进度与界面复活条同源：存活时只报累计死亡次数
    private void OnAiReviveProgress(AiRequest request)
    {
        var page = Tool.UIManager != null ? Tool.UIManager.Battle : null;
        if (page == null)
        {
            request.Reject("战斗界面未就绪");
            return;
        }

        var builder = new StringBuilder();
        AppendReviveProgress(builder, page);
        request.Reply(builder.ToString());
    }

    private static void AppendReviveProgress(StringBuilder builder, BattlePage page)
    {
        if (page == null)
        {
            builder.Append("战斗界面未就绪");
            return;
        }
        if (!page.AiReviveActive)
        {
            builder.Append("状态=存活 死亡次数=").Append(page.AiReviveDeathCount);
            return;
        }

        float duration = page.AiReviveDuration;
        float elapsed = Mathf.Clamp(page.AiReviveElapsed, 0f, duration);
        float ratio = duration > 0f ? elapsed / duration : 1f;
        builder.Append("状态=复活中 死亡次数=").Append(page.AiReviveDeathCount)
            .Append(" 已等待=").Append(elapsed.ToString("0.##"))
            .Append(" 总时长=").Append(duration.ToString("0.##"))
            .Append(" 剩余=").Append((duration - elapsed).ToString("0.##"))
            .Append(" 进度=").Append((ratio * 100f).ToString("0")).Append('%');
    }

    private static void AppendLevel(StringBuilder builder)
    {
        var battle = Tool.UIManager != null ? Tool.UIManager.Battle : null;
        if (battle == null) return;
        int level = battle.AiCardLevel;
        int need = CharacterExpConfig.NeedExp(level + 1);
        builder.Append(" 等级=").Append(level)
            .Append(" 经验=").Append(battle.AiCardExp).Append('/').Append(need > 0 ? need.ToString() : "MAX");
    }

    private void OnAiMinimapEntities(AiRequest request)
    {
        if (aiMinimap.Count == 0)
        {
            request.Reply("（小地图上没有实体）");
            return;
        }

        var ids = new List<ushort>(aiMinimap.Keys);
        ids.Sort();
        var builder = new StringBuilder();
        for (int i = 0; i < ids.Count; i++)
        {
            var entity = aiMinimap[ids[i]];
            if (builder.Length > 0) builder.Append('\n');
            builder.Append("id=").Append(entity.entityId)
                .Append(" 类型=").Append(entity.type.category).Append(':').Append(entity.type.value)
                .Append(" 阵营=").Append(entity.camp)
                .Append(" 坐标=(").Append(entity.posX.ToString("0.##")).Append(',').Append(entity.posZ.ToString("0.##")).Append(')')
                .Append(" 标记=").Append(entity.marked ? 1 : 0);
        }
        request.Reply(builder.ToString());
    }

    // 重要事件 = 战斗中技能播报那条 banner，玩家看得到的就这些
    private void OnAiImportantEvent(AiRequest request)
    {
        var line = AiCommandLine.Current;
        string text = line != null ? line.ReadImportant(ImportantReadCount) : null;
        request.Reply(text ?? "（没有重要事件）");
    }

    // 自身 + 守护点 + 阵营人数 + 昼夜 + 小地图条数，一次取全现场
    private void OnAiGetInfo(AiRequest request)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession)
        {
            request.Reject("没有战斗会话");
            return;
        }
        if (!AiTryGetSelfPos(out Vector3 pos))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("自身 实体=").Append(loading.OwnEntityId)
            .Append(" 阵营=").Append(loading.Camp)
            .Append(" 存活=").Append(aiSelfHealth > 0 ? "是" : "否")
            .Append(" 生命=").Append(aiSelfHealth).Append('/').Append(aiSelfMaxHealth);
        AppendLevel(builder);
        builder.Append(" 坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(AiSelfYaw().ToString("0.#"));

        builder.Append("\n复活 ");
        AppendReviveProgress(builder, Tool.UIManager != null ? Tool.UIManager.Battle : null);

        builder.Append("\n守护点 ");
        if (!aiHasGuardProgress) builder.Append("未收到");
        else
        {
            builder.Append("主=").Append(aiGuardProgress.mainHealth)
                .Append(" 副1=").Append(aiGuardProgress.sub1Health)
                .Append(" 副2=").Append(aiGuardProgress.sub2Health)
                .Append(" 副3=").Append(aiGuardProgress.sub3Health);
        }

        builder.Append("\n阵营 ");
        var camp = NetworkManager.LatestCampAliveCount;
        if (!NetworkManager.HasCampAliveCount) builder.Append("未收到");
        else
        {
            builder.Append("进攻=").Append(camp.attackerAlive).Append('/').Append(camp.attackerTotal)
                .Append(" 防守=").Append(camp.defenseAlive).Append('/').Append(camp.defenseTotal)
                .Append(" 观战=").Append(camp.spectatorCount);
        }

        builder.Append("\n昼夜 ").Append(IsDay ? "白天" : "夜晚")
            .Append(" 周期时间=").Append(CycleTime.ToString("0.##"));

        builder.Append("\n小地图 ").Append(aiMinimap.Count);

        builder.Append("\n卡牌 ");
        AppendCardCount(builder);

        request.Reply(builder.ToString());
    }

    private void OnAiGuardProgress(AiRequest request)
    {
        if (!aiHasGuardProgress)
        {
            request.Reject("尚未收到守护点数据");
            return;
        }
        request.Reply($"主={aiGuardProgress.mainHealth} 副1={aiGuardProgress.sub1Health} 副2={aiGuardProgress.sub2Health} 副3={aiGuardProgress.sub3Health}（当前血量，上限 = entity.csv 生命 × 人数倍率）");
    }

    private void OnAiDayNightInfo(AiRequest request)
    {
        request.Reply($"昼夜={(IsDay ? "白天" : "夜晚")} 周期时间={CycleTime:0.##}（0~2，0/2 午夜、1 正午）");
    }

    private void OnAiCampCount(AiRequest request)
    {
        var camp = NetworkManager.LatestCampAliveCount;
        if (!NetworkManager.HasCampAliveCount)
        {
            request.Reject("尚未收到阵营人数");
            return;
        }
        request.Reply($"进攻={camp.attackerAlive}/{camp.attackerTotal} 防守={camp.defenseAlive}/{camp.defenseTotal} 观战={camp.spectatorCount}");
    }

    // 三档各自的待抽次数
    private void OnAiCardCount(AiRequest request)
    {
        var builder = new StringBuilder();
        AppendCardCount(builder);
        request.Reply(builder.ToString());
    }

    private static void AppendCardCount(StringBuilder builder)
    {
        var view = Tool.UIManager != null ? Tool.UIManager.SelectCard : null;
        if (view == null)
        {
            builder.Append("抽卡界面未就绪");
            return;
        }
        builder.Append("白银=").Append(view.AiPendingCount((int)CardTier.Silver))
            .Append(" 黄金=").Append(view.AiPendingCount((int)CardTier.Gold))
            .Append(" 炫彩=").Append(view.AiPendingCount((int)CardTier.Holo));
    }

    private void OnAiSetCameraRender(AiRequest request)
    {
        bool on = request.Bool(0);
        var cam = Tool.CameraController != null ? Tool.CameraController.WorldCamera : null;
        if (cam == null)
        {
            request.Reject("相机未就绪");
            return;
        }
        cam.enabled = on;
    }

    private void OnAiScreenshot(AiRequest request)
    {
        var cam = Tool.CameraController != null ? Tool.CameraController.WorldCamera : null;
        if (cam == null)
        {
            request.Reject("相机未就绪");
            return;
        }
        if (!AiTryGetSelfPos(out Vector3 pos))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }

        string dir = Path.Combine(Application.dataPath, "..", ScreenshotFolder);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "ai_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");

        bool restore = !cam.enabled;
        if (restore) cam.enabled = true;
        ScreenCapture.CaptureScreenshot(file);
        if (restore) Timer.AddTimer(this, _ => RestoreAiCamera(cam), CameraRestoreDelay, 1, false);

        request.Reply($"文件={Path.GetFullPath(file)} 坐标=({pos.x:0.##},{pos.z:0.##}) 朝向={AiSelfYaw():0.#}");
    }

    private static void RestoreAiCamera(Camera cam)
    {
        if (cam != null) cam.enabled = false;
    }
    #endregion
}
