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

    private readonly Dictionary<ushort, SCMinimapEntity> aiMinimap = new();
    private readonly Dictionary<ushort, float> aiMinimapSeen = new();
    private readonly Dictionary<ushort, AiEntityState> aiEntityStates = new();
    private readonly List<ushort> aiMinimapExpired = new();
    private readonly List<string> aiSelfBuffs = new();

    // 可见实体的血量/buff 缓存：显示包按脏标记下发，没带的字段沿用上次的值
    private sealed class AiEntityState
    {
        public int health;
        public int maxHealth;
        public bool hasHealth;
        public readonly List<string> buffs = new();
        public bool hasBuffs;
        public float seen;
    }

    private struct NearbyEntry
    {
        public float distance;
        public string text;
    }

    // 阵营分档计数：摘要不把各阵营混成一个总数，进攻/防守/中立分开数
    private struct CampTally
    {
        public int attack;
        public int defense;
        public int neutral;
        public int none;

        public int Total => attack + defense + neutral + none;

        public void Add(EntityCamp camp)
        {
            if ((camp & EntityCamp.Attack) != 0) attack++;
            else if ((camp & EntityCamp.Defense) != 0) defense++;
            else if ((camp & EntityCamp.Neutral) != 0) neutral++;
            else none++;
        }

        public void AppendTo(StringBuilder builder)
        {
            builder.Append("（进攻=").Append(attack)
                .Append(" 防守=").Append(defense)
                .Append(" 中立=").Append(neutral);
            if (none > 0) builder.Append(" 未定=").Append(none);
            builder.Append('）');
        }
    }

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

        AiCommandBus.Bind(ai_commandEnum.control_stop, OnAiStop);
        AiCommandBus.Bind(ai_commandEnum.control_moveTo, OnAiMoveTo);
        AiCommandBus.Bind(ai_commandEnum.control_castSkill, OnAiCastSkill);
        AiCommandBus.Bind(ai_commandEnum.luaSM_start, OnAiStartLua);
        AiCommandBus.Bind(ai_commandEnum.luaSM_stop, OnAiStopLua);
        AiCommandBus.Bind(ai_commandEnum.luaSM_list, OnAiListLua);
        AiCommandBus.Bind(ai_commandEnum.terrain_points, OnAiPoints);
        AiCommandBus.Bind(ai_commandEnum.terrain_path, OnAiPath);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_all, OnAiGetInfo);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_common, OnAiCommon);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_timeLeft, OnAiTimeLeft);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_player, OnAiPlayer);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_reviveProgress, OnAiReviveProgress);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_pos, OnAiPos);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_guardProgress, OnAiGuardProgress);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_minimapEntities, OnAiMinimapEntities);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_dayNightInfo, OnAiDayNightInfo);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_camp, OnAiCampCount);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_cardCount, OnAiCardCount);
        AiCommandBus.Bind(ai_commandEnum.battleInfo_nearby, OnAiNearby);
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

        AiCommandBus.Unbind(ai_commandEnum.control_stop, OnAiStop);
        AiCommandBus.Unbind(ai_commandEnum.control_moveTo, OnAiMoveTo);
        AiCommandBus.Unbind(ai_commandEnum.control_castSkill, OnAiCastSkill);
        AiCommandBus.Unbind(ai_commandEnum.luaSM_start, OnAiStartLua);
        AiCommandBus.Unbind(ai_commandEnum.luaSM_stop, OnAiStopLua);
        AiCommandBus.Unbind(ai_commandEnum.luaSM_list, OnAiListLua);
        AiCommandBus.Unbind(ai_commandEnum.terrain_points, OnAiPoints);
        AiCommandBus.Unbind(ai_commandEnum.terrain_path, OnAiPath);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_all, OnAiGetInfo);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_common, OnAiCommon);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_timeLeft, OnAiTimeLeft);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_player, OnAiPlayer);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_reviveProgress, OnAiReviveProgress);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_pos, OnAiPos);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_guardProgress, OnAiGuardProgress);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_minimapEntities, OnAiMinimapEntities);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_dayNightInfo, OnAiDayNightInfo);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_camp, OnAiCampCount);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_cardCount, OnAiCardCount);
        AiCommandBus.Unbind(ai_commandEnum.battleInfo_nearby, OnAiNearby);
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

    // 显示包对每个客户端可见实体都会来：自身留一份，其余进「周围可见」的缓存
    private void OnAiEntityDisplay(SCEntityDisplayInfo info)
    {
        var state = EntityState(info.entityId);
        state.seen = Time.time;
        if (info.hasHealth)
        {
            state.health = info.health;
            state.maxHealth = info.maxHealth;
            state.hasHealth = true;
        }
        if (info.hasBuffs)
        {
            state.buffs.Clear();
            for (int i = 0; i < info.buffs.Count; i++)
            {
                state.buffs.Add(info.buffs[i].type + ":" + info.buffs[i].level);
            }
            state.hasBuffs = true;
        }

        var loading = ClientLoadingManager.Current;
        if (loading == null || info.entityId != loading.OwnEntityId) return;

        if (state.hasHealth)
        {
            aiSelfHealth = state.health;
            aiSelfMaxHealth = state.maxHealth;
        }
        if (!state.hasBuffs) return;

        aiSelfBuffs.Clear();
        for (int i = 0; i < state.buffs.Count; i++) aiSelfBuffs.Add(state.buffs[i]);
    }

    private AiEntityState EntityState(ushort id)
    {
        if (aiEntityStates.TryGetValue(id, out var state)) return state;
        state = new AiEntityState();
        aiEntityStates.Add(id, state);
        return state;
    }

    private void OnAiGuardUpdate(SCDefensePointProgress info)
    {
        aiGuardProgress = info;
        aiHasGuardProgress = true;
    }

    // 小地图与实体状态缓存都按 minimap_entity_timeout 剔除，ManagedUpdate 每帧推一次
    private void AiPerceptionTick()
    {
        float timeout = GlobalConfig.minimap_entity_timeout;

        aiMinimapExpired.Clear();
        foreach (var pair in aiMinimapSeen)
        {
            if (Time.time - pair.Value <= timeout) continue;
            aiMinimapExpired.Add(pair.Key);
        }
        for (int i = 0; i < aiMinimapExpired.Count; i++)
        {
            ushort id = aiMinimapExpired[i];
            aiMinimapSeen.Remove(id);
            aiMinimap.Remove(id);
        }

        if (aiEntityStates.Count == 0) return;

        // 血量/buff 是脏标记下发的，静止实体可能很久没有新包：视图还在就保留缓存，视图没了才回收
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        aiMinimapExpired.Clear();
        foreach (var pair in aiEntityStates)
        {
            if (Time.time - pair.Value.seen <= timeout) continue;
            if (players != null && players.GetView(pair.Key) != null) continue;
            aiMinimapExpired.Add(pair.Key);
        }
        for (int i = 0; i < aiMinimapExpired.Count; i++) aiEntityStates.Remove(aiMinimapExpired[i]);
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

    #region 直接操控（执行即停用 lua 状态机）
    // 直接操控优先：动手前先停掉 lua 决策机，否则下一拍就被状态机覆盖
    private static void StopLuaDecision()
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            Debug.LogError("[AI] AiLuaInterface 未就绪，control 命令无法停用 lua 状态机");
            return;
        }
        lua.StopDecision();
    }

    private void OnAiStop(AiRequest request)
    {
        StopLuaDecision();
        SendAiIntent(AiIntent.Stop, Vector2.zero, 0, request);
    }

    private void OnAiMoveTo(AiRequest request)
    {
        StopLuaDecision();
        SendAiIntent(AiIntent.MoveTo, new Vector2(request.Float(0), request.Float(1)), 0, request);
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
        StopLuaDecision();
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
        request.Reply(lua.ActivateStateMachine(request.Str(0), request.Str(1)));
    }

    private void OnAiListLua(AiRequest request)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            request.Reject("Lua 接口未就绪");
            return;
        }

        List<string> names = lua.ListStateMachines();
        if (names.Count == 0)
        {
            request.Reply("（没有可用的状态机脚本）");
            return;
        }

        var builder = new StringBuilder();
        for (int i = 0; i < names.Count; i++)
        {
            if (i > 0) builder.Append('\n');
            builder.Append(names[i]);
        }
        request.Reply(builder.ToString());
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
        if (!NavMesh.SamplePosition(to, out var endHit, 4f, NavMesh.AllAreas))
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
        builder.Append('\n').Append("进攻出生=随机（距地图中心 > ").Append((GlobalConfig.attack_spawn_min_ratio * TerrainSpawns.MapSize).ToString("0.#")).Append(" 米）");
        builder.Append('\n').Append("防守出生=主守护点 ").Append(GlobalConfig.defense_spawn_radius.ToString("0.#")).Append(" 米内随机");
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

    private void OnAiTimeLeft(AiRequest request)
    {
        request.Reply("剩余=" + Tool.ClientLogicManager.BattleTime.RemainTime.ToString("0.##") + "秒");
    }

    // 常用信息：位置 + 可见实体摘要 + 卡牌数量 + 聊天未读，AI 常轮询这条
    private void OnAiCommon(AiRequest request)
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
        var commands = AiCommandLine.Current;
        if (commands == null)
        {
            Debug.LogError("[AI] AiCommandLine 未就绪，battleInfo_common 取不到聊天未读");
            request.Reject("AI 命令行未就绪");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(AiSelfYaw().ToString("0.#"));

        builder.Append("\n可见实体 ");
        AppendVisibleSummary(builder);

        builder.Append("\n卡牌 ");
        AppendCardCount(builder);

        builder.Append("\n聊天未读 ").Append(commands.ReadChatUnread() ?? "（无）");

        request.Reply(builder.ToString());
    }

    // 客户端可见实体 = 已经有视图的实体（玩家看得到模型的那批），血量/buff 取显示包缓存
    private void OnAiNearby(AiRequest request)
    {
        if (!AiTryGetSelfPos(out Vector3 self))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players == null)
        {
            request.Reject("实体管理器未就绪");
            return;
        }
        var loading = ClientLoadingManager.Current;
        ushort selfId = loading != null && loading.HasSession ? loading.OwnEntityId : (ushort)0;

        var entries = new List<NearbyEntry>();
        var line = new StringBuilder();
        foreach (var view in players.AllViews)
        {
            if (view == null || view.id == selfId) continue;

            Vector3 at = view.transform.position;
            float dx = at.x - self.x;
            float dz = at.z - self.z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);

            line.Clear();
            line.Append("id=").Append(view.id)
                .Append(" 类型=").Append(view.type.category).Append(':').Append(view.type.value)
                .Append(" 阵营=").Append(view.camp)
                .Append(" 距离=").Append(distance.ToString("0.#"))
                .Append(" 坐标=(").Append(at.x.ToString("0.##")).Append(',').Append(at.z.ToString("0.##")).Append(')');

            if (aiEntityStates.TryGetValue(view.id, out var state))
            {
                line.Append(" 血量=");
                if (state.hasHealth) line.Append(state.health).Append('/').Append(state.maxHealth);
                else line.Append("未知");
                line.Append(" buff=").Append(state.hasBuffs ? BuffText(state.buffs) : "未知");
            }
            else line.Append(" 血量=未知 buff=未知");

            entries.Add(new NearbyEntry { distance = distance, text = line.ToString() });
        }

        if (entries.Count == 0)
        {
            request.Reply("（周围没有可见实体）");
            return;
        }

        entries.Sort((a, b) => a.distance.CompareTo(b.distance));
        var builder = new StringBuilder();
        builder.Append("自身=(").Append(self.x.ToString("0.##")).Append(',').Append(self.z.ToString("0.##"))
            .Append(") 可见=").Append(entries.Count);
        for (int i = 0; i < entries.Count; i++) builder.Append('\n').Append(entries[i].text);
        request.Reply(builder.ToString());
    }

    private static string BuffText(List<string> buffs)
    {
        if (buffs.Count == 0) return "无";
        return string.Join(",", buffs);
    }

    // 小地图实体按阵营分档
    private CampTally MinimapTally()
    {
        var tally = new CampTally();
        foreach (var pair in aiMinimap) tally.Add(pair.Value.camp);
        return tally;
    }

    // 可见实体按阵营分档（去掉自己）：与 battleInfo_nearby 同一口径，管理器没就绪返回 false
    private static bool TryVisibleTally(out CampTally tally)
    {
        tally = new CampTally();
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players == null) return false;
        var loading = ClientLoadingManager.Current;
        ushort selfId = loading != null && loading.HasSession ? loading.OwnEntityId : (ushort)0;

        foreach (var view in players.AllViews)
        {
            if (view == null || view.id == selfId) continue;
            tally.Add(view.camp);
        }
        return true;
    }

    // 摘要写法：总数 + 各阵营分档，battleInfo_all / battleInfo_common 共用
    private void AppendMinimapSummary(StringBuilder builder)
    {
        var tally = MinimapTally();
        builder.Append(tally.Total);
        tally.AppendTo(builder);
    }

    private static void AppendVisibleSummary(StringBuilder builder)
    {
        if (!TryVisibleTally(out var tally))
        {
            builder.Append("未就绪");
            return;
        }
        builder.Append(tally.Total);
        tally.AppendTo(builder);
    }

    // 自身 + 守护点 + 阵营人数 + 昼夜 + 小地图条数 + 三条缓冲的未读，一次取全现场
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
        var commands = AiCommandLine.Current;
        if (commands == null)
        {
            Debug.LogError("[AI] AiCommandLine 未就绪，battleInfo_all 取不到未读缓冲");
            request.Reject("AI 命令行未就绪");
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

        builder.Append("\n小地图 ");
        AppendMinimapSummary(builder);

        builder.Append("\n卡牌 ");
        AppendCardCount(builder);

        builder.Append("\n剩余时间 ").Append(Tool.ClientLogicManager.BattleTime.RemainTime.ToString("0.##")).Append('秒');
        builder.Append("\n可见实体 ");
        AppendVisibleSummary(builder);
        builder.Append("\n飘字未读 ").Append(commands.ReadPromptUnread() ?? "（无）");
        builder.Append("\n聊天未读 ").Append(commands.ReadChatUnread() ?? "（无）");
        builder.Append("\n重要播报未读 ").Append(commands.ReadImportantUnread() ?? "（无）");

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
