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

        AiCommandBus.Bind(AiCommandId.MoveTo, OnAiMoveTo);
        AiCommandBus.Bind(AiCommandId.CollectCoral, OnAiCollectCoral);
        AiCommandBus.Bind(AiCommandId.CastSkill, OnAiCastSkill);
        AiCommandBus.Bind(AiCommandId.CalculatePath, OnAiCalculatePath);
        AiCommandBus.Bind(AiCommandId.StartLuaStateMachine, OnAiStartLua);
        AiCommandBus.Bind(AiCommandId.StopLuaStateMachine, OnAiStopLua);
        AiCommandBus.Bind(AiCommandId.GetSkillList, OnAiSkillList);
        AiCommandBus.Bind(AiCommandId.GetInfo, OnAiGetInfo);
        AiCommandBus.Bind(AiCommandId.CurrentPos, OnAiCurrentPos);
        AiCommandBus.Bind(AiCommandId.GetSelfStatus, OnAiSelfStatus);
        AiCommandBus.Bind(AiCommandId.GetGuardProgress, OnAiGuardProgress);
        AiCommandBus.Bind(AiCommandId.GetMinimapEntities, OnAiMinimapEntities);
        AiCommandBus.Bind(AiCommandId.GetDayNightInfo, OnAiDayNightInfo);
        AiCommandBus.Bind(AiCommandId.GetCampCount, OnAiCampCount);
        AiCommandBus.Bind(AiCommandId.GetPointInfo, OnAiPointInfo);
        AiCommandBus.Bind(AiCommandId.SetCameraRender, OnAiSetCameraRender);
        AiCommandBus.Bind(AiCommandId.Screenshot, OnAiScreenshot);

        AiCommandBus.BindIntent(AiIntent.Stand, OnAiIntentStand);
        AiCommandBus.BindIntent(AiIntent.MoveTo, OnAiIntentMove);
        AiCommandBus.BindIntent(AiIntent.AttackTarget, OnAiIntentAttack);
        AiCommandBus.BindIntent(AiIntent.Stop, OnAiIntentStop);
    }

    private void UnbindAiCommands()
    {
        if (!aiCommandsBound) return;
        aiCommandsBound = false;

        AiCommandBus.Unbind(AiCommandId.MoveTo, OnAiMoveTo);
        AiCommandBus.Unbind(AiCommandId.CollectCoral, OnAiCollectCoral);
        AiCommandBus.Unbind(AiCommandId.CastSkill, OnAiCastSkill);
        AiCommandBus.Unbind(AiCommandId.CalculatePath, OnAiCalculatePath);
        AiCommandBus.Unbind(AiCommandId.StartLuaStateMachine, OnAiStartLua);
        AiCommandBus.Unbind(AiCommandId.StopLuaStateMachine, OnAiStopLua);
        AiCommandBus.Unbind(AiCommandId.GetSkillList, OnAiSkillList);
        AiCommandBus.Unbind(AiCommandId.GetInfo, OnAiGetInfo);
        AiCommandBus.Unbind(AiCommandId.CurrentPos, OnAiCurrentPos);
        AiCommandBus.Unbind(AiCommandId.GetSelfStatus, OnAiSelfStatus);
        AiCommandBus.Unbind(AiCommandId.GetGuardProgress, OnAiGuardProgress);
        AiCommandBus.Unbind(AiCommandId.GetMinimapEntities, OnAiMinimapEntities);
        AiCommandBus.Unbind(AiCommandId.GetDayNightInfo, OnAiDayNightInfo);
        AiCommandBus.Unbind(AiCommandId.GetCampCount, OnAiCampCount);
        AiCommandBus.Unbind(AiCommandId.GetPointInfo, OnAiPointInfo);
        AiCommandBus.Unbind(AiCommandId.SetCameraRender, OnAiSetCameraRender);
        AiCommandBus.Unbind(AiCommandId.Screenshot, OnAiScreenshot);

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

    private void SendAiIntent(AiIntent intent, Vector2 position, ushort targetId, AiRequest request)
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
        request.Reply("已下发 " + intent);
    }
    #endregion

    #region 战斗操作
    private void OnAiMoveTo(AiRequest request) =>
        SendAiIntent(AiIntent.MoveTo, new Vector2(request.Float(0), request.Float(1)), 0, request);

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
        request.Reply("已释放技能 " + skillId);
    }

    private void OnAiCalculatePath(AiRequest request)
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

    private bool TryAiFindNearestCoral(out ushort coralId)
    {
        coralId = 0;
        if (!AiTryGetSelfPos(out Vector3 self)) return false;

        float best = float.MaxValue;
        bool found = false;
        foreach (var pair in aiMinimap)
        {
            var entity = pair.Value;
            if (entity.type.category != EntityCategory.Coral) continue;
            float dx = entity.posX - self.x;
            float dz = entity.posZ - self.z;
            float sqr = dx * dx + dz * dz;
            if (sqr >= best) continue;
            best = sqr;
            coralId = entity.entityId;
            found = true;
        }
        return found;
    }
    #endregion

    #region 战斗查询
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

    private void OnAiSkillList(AiRequest request)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null)
        {
            request.Reject("技能管理器未就绪");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("槽位=").Append(skills.SlotCapacity);
        var active = skills.ActiveSkills;
        for (int i = 0; i < active.Count; i++)
        {
            int skillId = active[i];
            builder.Append('\n').Append("槽位").Append(i)
                .Append(": id=").Append(skillId)
                .Append(" 进度=").Append(skills.Recharge.GetCycleProgress(skillId).ToString("0.##"))
                .Append(" 库存=").Append(skills.Recharge.GetStore(skillId));
        }

        builder.Append("\n槽位外持有:");
        bool any = false;
        var held = skills.HeldSkills;
        for (int i = 0; i < held.Count; i++)
        {
            if (ContainsSkill(active, held[i])) continue;
            builder.Append(' ').Append(held[i]);
            any = true;
        }
        if (!any) builder.Append(" 无");
        request.Reply(builder.ToString());
    }

    private static bool ContainsSkill(IReadOnlyList<int> ids, int skillId)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == skillId) return true;
        }
        return false;
    }

    private void OnAiCurrentPos(AiRequest request)
    {
        if (!AiTryGetSelfPos(out Vector3 pos))
        {
            request.Reject("本地玩家实体未就绪");
            return;
        }
        request.Reply($"坐标=({pos.x:0.##},{pos.z:0.##})");
    }

    private void OnAiSelfStatus(AiRequest request)
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
            .Append(" 血量=").Append(aiSelfHealth).Append('/').Append(aiSelfMaxHealth)
            .Append(" 坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(AiSelfYaw().ToString("0.#"));

        builder.Append(" buff=");
        if (aiSelfBuffs.Count == 0) builder.Append("无");
        for (int i = 0; i < aiSelfBuffs.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(aiSelfBuffs[i]);
        }
        request.Reply(builder.ToString());
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
            .Append(" 血量=").Append(aiSelfHealth).Append('/').Append(aiSelfMaxHealth)
            .Append(" 坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(AiSelfYaw().ToString("0.#"));

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

    private void OnAiPointInfo(AiRequest request)
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
        request.Reply(on ? "相机渲染已开启" : "相机渲染已关闭（截屏会自动开启一帧）");
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
