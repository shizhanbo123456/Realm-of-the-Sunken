using System.Collections.Generic;
using System.Linq;
using Ros.Transport;
using TerrainGen;
using UnityEngine;
using UnityEngine.Profiling;

using Ros.Config;
public partial class BattleManager : EnsBehaviour
{
    private void Awake()
    {
        Tool.BattleManager = this;
        ApplyConfigDurations();
        SubscribeAiStage(); // 开战/结束决定 AI 命令的注册，顺带收集感知缓存
    }

    public static bool AtServer => EnsInstance.HasAuthority;

    public bool BattleStarted { get; private set; }

    public float BattleRemainTime { get; private set; }

    public int DefenseKills { get; private set; }
    public int AttackKills { get; private set; }
    public int AttackNeutralKills { get; private set; }
    public int DefenseNeutralKills { get; private set; }
    private float attackMainStatueDamage;
    private float attackSubStatueDamage;

    private Ticker syncTicker;
    private Ticker detailsTicker;
    private Ticker dayNightTicker;
    private int zombieCount;

    #region 玩家进出与组队大厅
    public readonly Dictionary<int, CSLobbyPlayerInfo> PlayerInfoList = new();
    public readonly Dictionary<int, PlayerCampType> PlayerCamps = new(); // 玩家阵营三分类（攻/守/观战），与实体阵营分离
    private readonly Dictionary<int, bool> readyByClient = new(); // 组队界面准备状态（真人专用）
    private Timer.TimerHandle startCountdownHandle; // 非 null 即开战倒计时进行中；取消时须 Cancel，否则残留任务会提前开局
    public readonly Dictionary<int, ushort> PlayerEntityId = new();
    public readonly Dictionary<ushort, int> EntityOwnerClient = new();

    // 人机玩家令牌段：与真人令牌（[1, 1e9)）不重叠，且 PlayerNameBase + token 仍在 int 内不溢出
    public const int ROBOT_TOKEN_BASE = 1_000_000_000;
    private int nextRobotToken = ROBOT_TOKEN_BASE;
    public readonly HashSet<int> RobotClients = new();
    // 本局还没用掉的名字：建人机时随机取走一个，取完多出来的人机退回 RobotN
    private readonly List<string> availableRobotNames = new();

    // 被外部 AI 接管的真人客户端：会话级，只在客户端退场时移除（实体重建不影响接管状态）
    public readonly HashSet<int> AiClients = new();

    // 加载阶段：已完成的客户端（战斗表现包只发给它们；观战未完成时不阻塞开战）
    private bool LoadingStarted;
    private readonly HashSet<int> loadedClients = new();
    // 加载期数据快照：客户端按偏移分批拉，偏移的稳定性靠它们（现算的话中途有人退场会错位）
    private readonly List<LoadingNameEntry> loadingNames = new();
    private readonly List<LoadingUnitEntry> loadingUnits = new();
    // 已预分配的实体 id（尚未 Spawn），分配器跳过它们，避免重复
    private readonly HashSet<ushort> reservedEntityIds = new();

    public int AttackRobotCount { get; private set; }
    public int DefenseRobotCount { get; private set; }

    public readonly Dictionary<int, float> HarvestByClient = new();

    public void AddHarvest(int clientId, float damage)
    {
        if (damage <= 0f) return;
        HarvestByClient.TryGetValue(clientId, out float sum);
        HarvestByClient[clientId] = sum + damage;
    }

    public EntityData GetTopHarvester()
    {
        int bestClient = -1;
        float bestValue = 0f;
        foreach (var pair in HarvestByClient)
        {
            if (pair.Value <= bestValue) continue;
            if (IsRobotToken(pair.Key)) continue; // 人机不参与采集榜首
            if (!PlayerEntityId.TryGetValue(pair.Key, out var entityId)) continue;
            if (!EntityContainer.Entities.TryGetObject(entityId, out var e) || e == null) continue;
            if (e.camp != EntityCamp.Attack) continue;
            bestValue = pair.Value;
            bestClient = pair.Key;
        }
        return bestClient >= 0 && PlayerEntityId.TryGetValue(bestClient, out var playerId)
            ? GetEntity(playerId)
            : null;
    }
    #endregion

    #region 实体容器（按分类，ChunkSearcher 区块加速）
    public static class EntityContainer
    {
        public static readonly ChunkSearcher<EntityData> Entities = new(d => d.transform.position);
        public static readonly ChunkSearcher<EntityData> GuardStatues = new(d => d.transform.position);
        public static readonly ChunkSearcher<EntityData> Corals = new(d => d.transform.position);
        public static readonly ChunkSearcher<EntityData> CrystalStatues = new(d => d.transform.position);

        public static void Clear()
        {
            Entities.Clear();
            GuardStatues.Clear();
            Corals.Clear();
            CrystalStatues.Clear();
        }

        private static readonly HashSet<int> s_buffer = new();

        // AI 侧索敌（不限方向）；技能索敌一律走带夹角的那个重载，夹角与半径读 skill.csv
        // 珊瑚垫底：先排除珊瑚找最近，范围内只有珊瑚时才锁珊瑚
        public static EntityData GetNearestEnemy(EntityData entity, float radius)
        {
            if (entity == null) return null;
            EntityCamp hostile = EntityCampUtil.HostileOf(entity.type.category);
            Vector3 pos = entity.transform.position;
            return GetNearestInCamp(pos, radius, hostile, entity.id, true)
                ?? GetNearestInCamp(pos, radius, hostile, entity.id);
        }

        // 索敌：夹角 <= skill_target_no_sector = 不索敌；> skill_target_full_sector = 不限方向；半径 > skill_target_unlimited_radius = 不限距离
        // 珊瑚垫底：先排除珊瑚找最近，范围内只有珊瑚时才锁珊瑚
        public static EntityData GetNearestEnemy(EntityData entity, float radius, float sectorHalfAngle)
        {
            if (entity == null || sectorHalfAngle <= GlobalConfig.skill_target_no_sector) return null;
            Vector3 pos = entity.transform.position;
            Vector3 fwd = entity.transform.forward;
            fwd.y = 0f;
            bool inSector = sectorHalfAngle <= GlobalConfig.skill_target_full_sector;
            if (inSector && fwd.sqrMagnitude < 1e-6f) return null; // 朝向退化时不猜方向
            fwd.Normalize();
            float cosLimit = inSector ? Mathf.Cos(sectorHalfAngle * Mathf.Deg2Rad) : -1f;
            EntityCamp hostile = EntityCampUtil.HostileOf(entity.type.category);
            return NearestInSector(pos, fwd, radius, cosLimit, inSector, hostile, entity.id, true)
                ?? NearestInSector(pos, fwd, radius, cosLimit, inSector, hostile, entity.id, false);
        }

        private static EntityData NearestInSector(Vector3 pos, Vector3 fwd, float radius, float cosLimit, bool inSector,
            EntityCamp hostile, ushort selfId, bool skipCoral)
        {
            bool unlimited = radius > GlobalConfig.skill_target_unlimited_radius;
            float sqrLimit = unlimited ? float.MaxValue : radius * radius;

            s_buffer.Clear();
            if (unlimited)
            {
                foreach (var id in Entities.Keys) s_buffer.Add(id); // 不限距离：连地图外的实体一起算
            }
            else
            {
                Entities.GetIdsInRange(pos, radius, s_buffer);
            }

            EntityData target = null;
            float bestSqr = float.MaxValue;
            foreach (var id in s_buffer)
            {
                if (!Entities.TryGetObject(id, out var e) || e == null) continue;
                if (e.id == selfId || (hostile & e.camp) == 0 || !e.Alive) continue;
                if (skipCoral && e.type.category == EntityCategory.Coral) continue;
                Vector3 to = e.transform.position - pos;
                to.y = 0f;
                float dsqr = to.sqrMagnitude;
                if (dsqr > sqrLimit) continue;
                if (inSector && dsqr < 1e-6f) continue; // 与自己重合时方向无意义
                if (Vector3.Dot(fwd, to.normalized) < cosLimit) continue;
                if (dsqr < bestSqr)
                {
                    bestSqr = dsqr;
                    target = e;
                }
            }
            s_buffer.Clear();
            return target;
        }

        public static EntityData GetNearestInCamp(Vector3 pos, float radius, EntityCamp camp, ushort excludeId = 0,
            bool skipCoral = false)
        {
            // 半径 > skill_target_unlimited_radius = 不限距离（与带夹角的重载同约定）
            if (radius > GlobalConfig.skill_target_unlimited_radius)
            {
                s_buffer.Clear();
                foreach (var id in Entities.Keys) s_buffer.Add(id); // 不限距离：连地图外的实体一起算
            }
            else
            {
                Entities.GetIdsInRange(pos, radius, s_buffer); // 内部先清空缓冲
            }
            EntityData target = null;
            float sqr = float.MaxValue;
            foreach (var id in s_buffer)
            {
                if (!Entities.TryGetObject(id, out var e)) continue;
                if (e.id == excludeId || (camp & e.camp) == 0 || !e.Alive) continue;
                if (skipCoral && e.type.category == EntityCategory.Coral) continue;
                float d = (e.transform.position - pos).sqrMagnitude;
                if (d < sqr)
                {
                    sqr = d;
                    target = e;
                }
            }
            s_buffer.Clear();
            return target;
        }

        public static void GetAllInCamp(Vector3 pos, float radius, EntityCamp camp, List<EntityData> outList)
        {
            outList.Clear();
            Entities.GetIdsInRange(pos, radius, s_buffer);
            foreach (var id in s_buffer)
            {
                if (!Entities.TryGetObject(id, out var e)) continue;
                if ((camp & e.camp) != 0 && e.Alive) outList.Add(e);
            }
            s_buffer.Clear();
        }
    }
    #endregion

    #region 实体生命周期
    private ushort nextEntityId = 1;

    public ushort SpawnEntity(EntityType type, int level, Vector3 pos, EntityCamp camp)
    {
        return SpawnEntity(AllocEntityId(), type, level, pos, camp);
    }

    // 用加载阶段预分配的 id 生成（id 由 BeginLoading 预留，客户端加载期拿到的就是它）
    public ushort SpawnEntity(ushort reservedId, EntityType type, int level, Vector3 pos, EntityCamp camp)
    {
        // 实体生成是服务器权威路径：客户端不挂 AssetsManager，走到这里说明服务器逻辑跑到了客户端上
        if (Tool.AssetsManager == null || !Tool.AssetsManager.TryGetGraphic(type, out var prefab) || prefab == null)
        {
            Debug.LogWarning($"缺少实体预制体：{type}，请先在 AssetsManager 配置对应 Graphics");
            return 0;
        }
        ushort id = reservedId;
        // 人形判定走预制体是否挂 EntityAnim（项目唯一口径）：人形离地抬升，出生点给的是地面高度，直接落上去会卡进地面
        if (prefab.GetComponentInChildren<EntityAnim>(true) != null) pos.y += GlobalConfig.entity_spawn_rise;
        var go = Instantiate(prefab, pos, Quaternion.identity);
        // EntityData 不挂预制体（图形预制体自带表现），生成时按类别补上对应子类
        var data = go.GetComponent<EntityData>();
        if (data == null) data = EntityData.AddTo(go, type.category);
        if (data == null)
        {
            Debug.LogError($"实体类别 {type.category} 没有对应的 EntityData 子类，无法生成");
            Destroy(go);
            return 0;
        }
        data.OnCreate(id, type, level, camp);
        // 初始技能表按实体类型统一赋（来源 entity.csv 初始技能列表列）：玩家角色与非玩家单位（僵尸/精英/雕像）同一条路径，
        // 表里没配即为空表
        if (data.skillController != null)
            data.skillController.SetSkillList(EntityConfig.GetByCategory(type).initialSkills);
        ApplyMinimapLostOnSpawn(data); // 夜间出生/复活：补上「小地图失联」（昼夜事件只在翻转那一刻遍历）
        AddToContainer(data);
        reservedEntityIds.Remove(id); // 已实体化，不再占用预留位
        return id;
    }

    private void ResetEntityIdSource() => nextEntityId = 1;

    private ushort AllocEntityId()
    {
        // 服务器实体 id 自增，0 保留；超过上限 30000 回绕到 1（跳过已占用与已预留）
        do
        {
            nextEntityId++;
            if (nextEntityId > GlobalConfig.entity_id_max) nextEntityId = 1;
        } while (EntityContainer.Entities.Contains(nextEntityId) || reservedEntityIds.Contains(nextEntityId));
        return nextEntityId;
    }

    public static EntityData GetEntity(ushort id) =>
        EntityContainer.Entities.TryGetObject(id, out var e) ? e : null;

    public bool DestroyEntity(ushort id)
    {
        if (!EntityContainer.Entities.TryGetObject(id, out var data)) return false;
        RemoveFromContainer(data);
        // 玩家实体销毁：清空其"玩家 → 实体"关系（死亡/离场/对局清理统一走此入口）
        if (EntityOwnerClient.TryGetValue(id, out var ownerId))
        {
            EntityOwnerClient.Remove(id);
            Tool.NetworkManager.SendPlayerEntity(ownerId, -1);
        }

        // 通知所有客户端移除该实体表现（死亡/离场/摧毁统一走此入口）
        foreach (var clientId in PlayerInfoList.Keys)
        {
            Tool.NetworkManager.SendRemoveEntity(clientId, id);
        }
        // 雕像被摧毁事件（事件列表 icon=其它）：textId 区分哪一座（国王 / 贤者一~三）
        if (BattleStarted && data.type.category == EntityCategory.GuardStatue)
        {
            MarkDefensePointDestroyed(data); // 实体销毁后算不出血量，进度在这里定死 100
            // 贤者雕像 1~3 的枚举成员连续，value 直接当偏移用
            var textId = data.type == EntityType.KingStatue
                ? system_messageEnum.king_statue_destroyed
                : system_messageEnum.sage_statue_1_destroyed + data.type.value;
            Tool.NetworkManager.SendBattleEvent(new SCBattleEvent
            {
                type = SCBattleEvent.Type.StatueDestroyed,
                textId = (int)textId,
            });
        }

        data.OnDestroyed();
        bool wasKingStatue = data.type.category == EntityCategory.GuardStatue && data.type == EntityType.KingStatue;
        Destroy(data.gameObject);
        if (wasKingStatue && BattleStarted)
        {
            EndBattle(1); // 主守护点被摧毁 → 进攻方胜，不看副守护点是否存活（策划案 17.2）
        }
        return true;
    }

    private static bool IsZombieCategory(EntityCategory category) =>
        category == EntityCategory.Zombie || category == EntityCategory.EliteZombie;

    private void AddToContainer(EntityData data)
    {
        EntityContainer.Entities.Add(data.id, data);
        switch (data.type.category)
        {
            case EntityCategory.GuardStatue:
                EntityContainer.GuardStatues.Add(data.id, data);
                break;
            case EntityCategory.Coral:
                EntityContainer.Corals.Add(data.id, data);
                break;
            case EntityCategory.CrystalStatue:
                EntityContainer.CrystalStatues.Add(data.id, data);
                break;
        }
        if (IsZombieCategory(data.type.category)) zombieCount++;
    }

    private void RemoveFromContainer(EntityData data)
    {
        EntityContainer.Entities.Remove(data.id);
        EntityContainer.GuardStatues.Remove(data.id);
        EntityContainer.Corals.Remove(data.id);
        EntityContainer.CrystalStatues.Remove(data.id);
        if (IsZombieCategory(data.type.category)) zombieCount--;
    }
    #endregion

    #region 玩家进出
    public void AddPlayer(int clientId, CSLobbyPlayerInfo info)
    {
        if (PlayerInfoList.ContainsKey(clientId))
        {
            NotifyPlayerOnline(clientId); // 同一令牌再次进房 = 断线重连，数据都还在，只需补发
            return;
        }
        NormalizeName(ref info, clientId);
        if (info.aiTakeover) AiClients.Add(clientId); // 连接时已声明 AI 接管：记进会话，出战与复活都按它恢复
        PlayerInfoList[clientId] = info;
        PlayerCamps[clientId] = PlayerCampType.Spectator; // 进入大厅默认观战方
        SendLobbyPlayerNames(clientId); // 名字单独下发，先把映射铺好再播房间成员列表
        SendLobbyInfoFull(clientId);    // 新玩家先拿全量快照，才能建立成员表
        BroadcastLobbyInfo();           // 其他人收增量（内容 = 新增此人）
        // 对局（含加载期）已开始才进房：本局没有参赛名额，按观战方补发一次完整会话
        if (LoadingStarted || BattleStarted) ResyncBattle(clientId);
        Debug.Log($"玩家 {clientId} 进入组队大厅");
    }

    // 名字按字符数裁剪：超上限截断，不足下限用默认名兜底（客户端已有同样限制，这里防绕过）
    private static void NormalizeName(ref CSLobbyPlayerInfo info, int clientId)
    {
        int max = GlobalVariable.I(global_variablesEnum.name_length_max);
        int min = GlobalVariable.I(global_variablesEnum.name_length_min);
        string name = (info.name ?? "").Trim();
        if (name.Length > max) name = name.Substring(0, max);
        info.name = name.Length < min ? MessageVariable.F(message_variablesEnum.default_playerName, clientId) : name;
    }

    // 新玩家进房：新玩家名字广播给所有人，再把房间里已有玩家的名字逐条发给新玩家（人机不下发名字）
    private void SendLobbyPlayerNames(int newClientId)
    {
        if (PlayerInfoList.TryGetValue(newClientId, out var me))
            Tool.NetworkManager.SendLobbyPlayerName(new SCLobbyPlayerName { clientId = newClientId, name = me.name });

        foreach (var pair in PlayerInfoList)
        {
            if (IsRobotToken(pair.Key)) continue; // 人机无名字
            Tool.NetworkManager.SendLobbyPlayerName(newClientId, new SCLobbyPlayerName { clientId = pair.Key, name = pair.Value.name });
        }
    }

    public void RemovePlayer(int clientId)
    {
        if (PlayerEntityId.TryGetValue(clientId, out var entityId))
        {
            DestroyEntity(entityId);
            PlayerEntityId.Remove(clientId);
        }
        ExitSpectate(clientId);  // 从被观战目标的列表摘除自己
        ClearSpectators(clientId); // 观战我的人失去目标（回到各自冻结视野）
        visionStates.Remove(clientId);
        readyByClient.Remove(clientId);
        loadedClients.Remove(clientId);
        PlayerInfoList.Remove(clientId);
        PlayerCamps.Remove(clientId);
        AiClients.Remove(clientId); // 退场即结束接管
        // 开始倒计时中有人退场：剩余真人不再全员 ready 则取消计时并提示（仍满足则继续倒计时）
        if (startCountdownHandle != null && !AllHumansReady())
        {
            CancelStartCountdown();
            SendPromptToAll(promptEnum.battle_start_canceled);
        }
        // 加载阶段有人退场：剩下的人若已全部回执，直接开战，不再等退场者
        if (LoadingStarted && !BattleStarted && AllCombatClientsLoaded()) StartBattle();
        if (!BattleStarted) BroadcastLobbyInfo();
        Debug.Log($"玩家 {clientId} 退场");
    }

    private void RebuildRobotPlayers()
    {
        foreach (var robotClientId in RobotClients)
        {
            PlayerInfoList.Remove(robotClientId);
            PlayerCamps.Remove(robotClientId);
        }
        RobotClients.Clear();
        availableRobotNames.Clear();
        availableRobotNames.AddRange(RobotNamesConfig.Names); // 重建即人机重新进场，名字池一并复位

        nextRobotToken = ROBOT_TOKEN_BASE;
        for (int i = 0; i < AttackRobotCount; i++) AddRobotPlayer(PlayerCampType.Attack);
        for (int i = 0; i < DefenseRobotCount; i++) AddRobotPlayer(PlayerCampType.Defense);
    }

    private void AddRobotPlayer(PlayerCampType camp)
    {
        int robotClientId = nextRobotToken++;
        var info = new CSLobbyPlayerInfo()
        {
            // 名字：按 robot_random_name 的概率取一个本局没用过的名字，未命中或名字用尽则用令牌推导的 RobotN
            name = RandomRobotName(robotClientId),
        };
        if (camp == PlayerCampType.Attack)
        {
            info.attackCharacter = EntityType.Attack(RandomRobotCharacter(EntityCategory.Character_Attack));
        }
        else
        {
            info.defenseCharacter = EntityType.Defense(RandomRobotCharacter(EntityCategory.Character_Defense));
        }
        PlayerInfoList[robotClientId] = info;
        PlayerCamps[robotClientId] = camp;
        RobotClients.Add(robotClientId);
    }

    // 人机只在「本阵营且配表标记可选」的角色里随机；没有可选角色属于配表错误
    private static int RandomRobotCharacter(EntityCategory category)
    {
        List<int> candidates = EntityConfig.RobotAvailableCharacters(category);
        if (candidates.Count == 0)
            throw new System.InvalidOperationException($"[配表] entity 的 {category} 没有任何人机可选角色，人机无法选角");
        return candidates[UnityEngine.Random.Range(0, candidates.Count)];
    }

    // robot_random_name 的值即「使用随机名称的概率」：命中且名字池还有剩就取一个没被用过的，否则退回编号名
    private string RandomRobotName(int robotClientId)
    {
        float chance = GlobalVariable.F(global_variablesEnum.robot_random_name);
        if (availableRobotNames.Count > 0 && UnityEngine.Random.value < chance)
        {
            int index = UnityEngine.Random.Range(0, availableRobotNames.Count);
            string name = availableRobotNames[index];
            availableRobotNames.RemoveAt(index);
            return name;
        }
        return $"Robot{robotClientId - ROBOT_TOKEN_BASE + 1}";
    }

    public void ReceiveLobbyUpdate(int clientId, CSLobbyUpdate update)
    {
        // 加载阶段与对局中都不再接受组队大厅的改动
        if (BattleStarted || LoadingStarted || !PlayerInfoList.ContainsKey(clientId)) return;

        // 选队（0 进攻 / 1 防守 / 2 观战；进入大厅默认观战，不存在无阵营状态），真人名额满则保持原阵营
        if (update.camp == (int)PlayerCampType.Attack && CanJoinCamp(clientId, PlayerCampType.Attack)) PlayerCamps[clientId] = PlayerCampType.Attack;
        else if (update.camp == (int)PlayerCampType.Defense && CanJoinCamp(clientId, PlayerCampType.Defense)) PlayerCamps[clientId] = PlayerCampType.Defense;
        else if (update.camp == (int)PlayerCampType.Spectator) PlayerCamps[clientId] = PlayerCampType.Spectator;

        // 准备状态（任意玩家可随时切换，全员准备后进入开战倒计时）
        SetReady(clientId, update.ready);

        // 人机数量：房间共享，任意玩家可编辑，上限按配表钳制。数量变化时重建人机玩家（= 人机进场随机选角）
        int attackRobot = Mathf.Clamp(update.attackRobotCount, 0, GlobalVariable.I(global_variablesEnum.player_limit_attacker_AI));
        int defenseRobot = Mathf.Clamp(update.defenseRobotCount, 0, GlobalVariable.I(global_variablesEnum.player_limit_defenser_AI));
        if (attackRobot != AttackRobotCount || defenseRobot != DefenseRobotCount)
        {
            AttackRobotCount = attackRobot;
            DefenseRobotCount = defenseRobot;
            RebuildRobotPlayers();
        }

        BroadcastLobbyInfo();
    }

    // 真人名额（不含人机）：已在该阵营直接放行，避免重复上报/重连被挡
    private bool CanJoinCamp(int clientId, PlayerCampType camp)
    {
        if (PlayerCamps.TryGetValue(clientId, out var current) && current == camp) return true;
        int limit = camp == PlayerCampType.Attack
            ? GlobalVariable.I(global_variablesEnum.player_limit_attacker)
            : GlobalVariable.I(global_variablesEnum.player_limit_defenser);
        int count = 0;
        foreach (var pair in PlayerCamps)
        {
            if (pair.Value == camp && !RobotClients.Contains(pair.Key)) count++;
        }
        return count < limit;
    }

    #region 准备与开局倒计时
    private bool AllHumansReady()
    {
        foreach (var clientId in PlayerInfoList.Keys)
        {
            if (RobotClients.Contains(clientId)) continue;
            if (!readyByClient.TryGetValue(clientId, out var ready) || !ready) return false;
        }
        return true;
    }

    private void SetReady(int clientId, bool ready)
    {
        if (BattleStarted || LoadingStarted || RobotClients.Contains(clientId) || !PlayerInfoList.ContainsKey(clientId)) return;
        readyByClient[clientId] = ready;
        if (!ready)
        {
            if (startCountdownHandle != null)
            {
                CancelStartCountdown();
                SendPromptToAll(promptEnum.battle_start_canceled); // 有玩家取消了准备
            }
            return;
        }
        if (startCountdownHandle != null || !AllHumansReady()) return;
        // 全员准备：双方（真人 + 人机）均需 > 0 才进入开始倒计时
        if (PlayerCamps.Values.Count(c => c == PlayerCampType.Attack) <= 0
            || PlayerCamps.Values.Count(c => c == PlayerCampType.Defense) <= 0)
        {
            SendPromptToAll(promptEnum.battle_need_both_camps);
            return;
        }
        SendPromptToAll(promptEnum.battle_starting); // 全员已准备，战斗即将开始
        startCountdownHandle = Timer.AddTimer(0, _ => TryFireStartBattle(), GlobalVariable.F(global_variablesEnum.ready_start_delay), 1, false);
    }

    // 业务取消：置空句柄并让已注册任务作废，重开倒计时才是全新计时
    private void CancelStartCountdown()
    {
        if (startCountdownHandle != null) startCountdownHandle.Cancel();
        startCountdownHandle = null;
    }

    // 倒计时到期：期间可能有人取消/退场，重新校验后进入加载阶段
    private void TryFireStartBattle()
    {
        if (startCountdownHandle == null) return;
        startCountdownHandle = null;
        if (BattleStarted || LoadingStarted) return;
        if (!AllHumansReady()
            || PlayerCamps.Values.Count(c => c == PlayerCampType.Attack) <= 0
            || PlayerCamps.Values.Count(c => c == PlayerCampType.Defense) <= 0)
        {
            SendPromptToAll(promptEnum.battle_need_both_camps);
            return;
        }
        BeginLoading();
    }

    #region 加载阶段
    // 加载阶段：先让客户端切加载界面，再预分配实体 id，等客户端逐步请求数据并回执完成后才真正开战
    private void BeginLoading()
    {
        if (LoadingStarted || BattleStarted) return;
        LoadingStarted = true;
        loadedClients.Clear();

        // 上局残留（结算期间保留实体展示）在这里统一清掉
        var stale = new List<EntityData>();
        foreach (var e in EntityContainer.Entities)
        {
            if (e != null) stale.Add(e);
        }
        foreach (var e in stale) DestroyEntity(e.id);

        ResetEntityIdSource(); // 本局唯一一次重置 id 源
        reservedEntityIds.Clear();
        PlayerEntityId.Clear();

        // ① 先切加载界面（观战方 camp = None）
        foreach (var pair in PlayerInfoList)
        {
            if (RobotClients.Contains(pair.Key)) continue;
            Tool.NetworkManager.SendLoadingStart(pair.Key, new SCLoadingStart { camp = CampOf(pair.Key) });
        }

        // ② 预分配实体 id：只登记不生成（提前生成会让实体动画在加载期就开始播）
        foreach (var pair in PlayerInfoList)
        {
            if (CampOf(pair.Key) == EntityCamp.None) continue; // 观战方无实体
            ushort id = AllocEntityId();
            reservedEntityIds.Add(id);
            PlayerEntityId[pair.Key] = id;
            EntityOwnerClient[id] = pair.Key;
        }

        // ③ 预处理完成：拍快照并下发条数，客户端按条数自己来拉
        RefreshLoadingSnapshots();
        foreach (var pair in PlayerInfoList)
        {
            if (RobotClients.Contains(pair.Key)) continue;
            PlayerEntityId.TryGetValue(pair.Key, out var ownId);
            bool isDefense = PlayerCamps.TryGetValue(pair.Key, out var camp) && camp == PlayerCampType.Defense;
            Tool.NetworkManager.SendLoadingPrepared(pair.Key, new SCLoadingPrepared
            {
                ownEntityId = ownId,
                // 观战方没有战斗角色，与旧行为一致地沿用其进攻方选择（结算取角色下标用）
                characterType = isDefense ? pair.Value.defenseCharacter : pair.Value.attackCharacter,
                serverTime = Time.time,
                nameCount = loadingNames.Count,
                unitCount = loadingUnits.Count,
            });
        }
        Debug.Log($"[加载] 开始：真人 {PlayerInfoList.Count - RobotClients.Count}，预留实体 id {reservedEntityIds.Count} 个");
    }

    private EntityCamp CampOf(int clientId)
    {
        if (!PlayerCamps.TryGetValue(clientId, out var camp)) return EntityCamp.None;
        if (camp == PlayerCampType.Attack) return EntityCamp.Attack;
        if (camp == PlayerCampType.Defense) return EntityCamp.Defense;
        return EntityCamp.None;
    }

    // 加载快照：名字覆盖真人 + 人机 + 观战方；单位只收有实体的（观战方无实体，只出现在名字表里）
    private void RefreshLoadingSnapshots()
    {
        loadingNames.Clear();
        loadingUnits.Clear();
        foreach (var pair in PlayerInfoList)
        {
            loadingNames.Add(new LoadingNameEntry { clientId = pair.Key, name = NameOf(pair.Key, pair.Value) });
            if (!PlayerEntityId.TryGetValue(pair.Key, out var entityId)) continue;
            loadingUnits.Add(new LoadingUnitEntry { clientId = pair.Key, entityId = entityId, camp = CampOf(pair.Key) });
        }
    }

    // 单包上限 256B：名字每包 6 条（6×(4 令牌+1 长度+18 名字)+4 偏移+4 计数 ≈ 146B）
    private const int LoadingNamesPerPacket = 6;
    // 单位每包 16 条（16×(4 令牌+2 实体+4 阵营)+8 ≈ 168B）
    private const int LoadingUnitsPerPacket = 16;

    // 客户端按偏移来要名字；偏移由快照保证稳定，超出范围返回空列表（表示没有更多）
    public void OnLoadingRequestNames(int clientId, CSLoadingNamesRequest request)
    {
        var chunk = new SCLoadingPlayerNames { offset = request.offset, list = new List<LoadingNameEntry>() };
        for (int i = request.offset; i < loadingNames.Count && chunk.list.Count < LoadingNamesPerPacket; i++)
        {
            chunk.list.Add(loadingNames[i]);
        }
        Tool.NetworkManager.SendLoadingPlayerNames(clientId, chunk);
    }

    // 同上：参战单位（真人 + 人机、各阵营混排）按偏移分批给
    public void OnLoadingRequestUnits(int clientId, CSLoadingUnitRequest request)
    {
        var chunk = new SCLoadingUnitResponse { offset = request.offset, list = new List<LoadingUnitEntry>() };
        for (int i = request.offset; i < loadingUnits.Count && chunk.list.Count < LoadingUnitsPerPacket; i++)
        {
            chunk.list.Add(loadingUnits[i]);
        }
        Tool.NetworkManager.SendLoadingUnitResponse(clientId, chunk);
    }

    // 人机令牌落在 [ROBOT_TOKEN_BASE, …) 段，与真人令牌段互不重叠
    public static bool IsRobotToken(int clientId) => clientId >= ROBOT_TOKEN_BASE;

    private static string NameOf(int clientId, CSLobbyPlayerInfo info)
    {
        if (!string.IsNullOrEmpty(info.name)) return info.name;
        return IsRobotToken(clientId)
            ? MessageVariable.F(message_variablesEnum.default_robotName, clientId - ROBOT_TOKEN_BASE + 1)
            : MessageVariable.F(message_variablesEnum.default_playerName, clientId);
    }

    // 攻守真人全部回执完成后才真正开战；观战方不阻塞
    private bool AllCombatClientsLoaded()
    {
        foreach (var pair in PlayerCamps)
        {
            if (RobotClients.Contains(pair.Key)) continue;
            if (pair.Value == PlayerCampType.Spectator) continue;
            if (!loadedClients.Contains(pair.Key)) return false;
        }
        return true;
    }

    public void OnClientLoadingComplete(int clientId)
    {
        if (RobotClients.Contains(clientId) || !PlayerInfoList.ContainsKey(clientId)) return;
        if (!LoadingStarted && !BattleStarted) return;
        if (!loadedClients.Add(clientId)) return;

        if (BattleStarted)
        {
            // 开战后才完成的观战方：补发开始战斗，表现数据由首见全量补上
            Tool.NetworkManager.SendBattleStart(clientId);
            SendSkillSyncFull(clientId); // 重连：技能表在进战斗页时清空，须在其后补全量
            return;
        }
        if (AllCombatClientsLoaded()) StartBattle();
    }
    #endregion

    private void SendPromptToAll(promptEnum messageId)
    {
        foreach (var clientId in PlayerInfoList.Keys) Tool.NetworkManager.SendPrompt(clientId, (int)messageId);
    }
    #endregion

    // 组队期成员快照：clientId → 上次广播的席位信息，用于算增量（首次广播前为空 = 全量自然下发）
    private readonly Dictionary<int, SCLobbyInfo.LobbyMemberInfo> lobbyMemberSnapshot = new();
    private int lobbySnapshotAttackRobot = -1;
    private int lobbySnapshotDefenseRobot = -1;
    private bool lobbySnapshotBattleStarted;
    private static readonly List<SCLobbyInfo.LobbyMemberInfo> s_lobbyMembers = new();

    // 全量快照：只发给指定客户端（进房 / 重连），供其建立成员表
    private void SendLobbyInfoFull(int clientId)
    {
        var info = new SCLobbyInfo
        {
            full = true,
            attackRobotCount = AttackRobotCount,
            defenseRobotCount = DefenseRobotCount,
            battleStarted = BattleStarted,
            members = new List<SCLobbyInfo.LobbyMemberInfo>(),
        };
        CollectLobbyMembers(info.members);
        Tool.NetworkManager.SendLobbyInfo(clientId, info);
    }

    private void CollectLobbyMembers(List<SCLobbyInfo.LobbyMemberInfo> into)
    {
        into.Clear();
        foreach (var pair in PlayerInfoList)
        {
            if (IsRobotToken(pair.Key)) continue; // 人机不进成员列表，只通过两个数量表达
            int campValue = PlayerCamps.TryGetValue(pair.Key, out var camp) ? (int)camp : (int)PlayerCampType.Spectator;
            //所选角色：按成员最终阵营取 CSLobbyPlayerInfo 里对应一侧（首页选择随加入上报）
            int characterIndex = -1;
            if (campValue == (int)PlayerCampType.Attack && pair.Value.attackCharacter.category == EntityCategory.Character_Attack)
                characterIndex = pair.Value.attackCharacter.value;
            else if (campValue == (int)PlayerCampType.Defense && pair.Value.defenseCharacter.category == EntityCategory.Character_Defense)
                characterIndex = pair.Value.defenseCharacter.value;
            readyByClient.TryGetValue(pair.Key, out var ready);
            into.Add(new SCLobbyInfo.LobbyMemberInfo()
            {
                clientId = pair.Key,
                camp = campValue,
                characterIndex = characterIndex,
                ready = ready,
            });
        }
    }

    // 增量：members 只带本次变化的成员（离开 = removed 条目），没有任何变化就不发
    private void BroadcastLobbyInfo()
    {
        CollectLobbyMembers(s_lobbyMembers);
        var info = new SCLobbyInfo
        {
            full = false,
            attackRobotCount = AttackRobotCount,
            defenseRobotCount = DefenseRobotCount,
            battleStarted = BattleStarted,
            members = new List<SCLobbyInfo.LobbyMemberInfo>(),
        };
        bool changed = AttackRobotCount != lobbySnapshotAttackRobot
            || DefenseRobotCount != lobbySnapshotDefenseRobot
            || BattleStarted != lobbySnapshotBattleStarted;

        foreach (var member in s_lobbyMembers)
        {
            if (!lobbyMemberSnapshot.TryGetValue(member.clientId, out var old)
                || old.camp != member.camp || old.characterIndex != member.characterIndex || old.ready != member.ready)
            {
                info.members.Add(member);
                changed = true;
            }
        }
        foreach (var pair in lobbyMemberSnapshot)
        {
            if (s_lobbyMembers.Exists(m => m.clientId == pair.Key)) continue;
            info.members.Add(new SCLobbyInfo.LobbyMemberInfo() { clientId = pair.Key, removed = true });
            changed = true;
        }

        lobbyMemberSnapshot.Clear();
        foreach (var member in s_lobbyMembers) lobbyMemberSnapshot[member.clientId] = member;
        lobbySnapshotAttackRobot = AttackRobotCount;
        lobbySnapshotDefenseRobot = DefenseRobotCount;
        lobbySnapshotBattleStarted = BattleStarted;

        if (changed) Tool.NetworkManager.SendLobbyInfo(info);
    }

    //聊天只转发不存历史；发送方名字/阵营/角色由服务器补齐（客户端不知道别人的）
    public void ReceiveChat(int clientId, CSChat chat)
    {
        var message = BuildChat(clientId, chat.channel, chat.content);
        if (chat.channel == (int)ChatChannel.Nearby)
        {
            SendNearbyChat(clientId, message);
            return;
        }
        if (chat.channel == (int)ChatChannel.Camp)
        {
            SendCampChat(clientId, message);
            return;
        }
        Tool.NetworkManager.SendChat(message);
    }

    // 阵营频道：只发给同阵营真人；发送方本地已回显，观战方不显示阵营消息
    private void SendCampChat(int senderClientId, SCChat message)
    {
        int camp = CampValueOf(senderClientId);
        foreach (int targetClientId in PlayerInfoList.Keys)
        {
            if (targetClientId == senderClientId) continue;
            if (RobotClients.Contains(targetClientId)) continue;
            if (CampValueOf(targetClientId) != camp) continue;
            Tool.NetworkManager.SendChat(targetClientId, message);
        }
    }

    // 附近频道：只发给「操控的实体」在发送者 chat_nearby_radius 米内的玩家；观战方没有实体，发不出也收不到
    private void SendNearbyChat(int senderClientId, SCChat message)
    {
        EntityData sender = GetEntityOfClient(senderClientId);
        if (sender == null) return;

        float radius = GlobalVariable.F(global_variablesEnum.chat_nearby_radius);
        Vector3 from = sender.transform.position;
        foreach (int targetClientId in PlayerInfoList.Keys)
        {
            if (targetClientId == senderClientId) continue; // 发送方本地已回显
            EntityData target = GetEntityOfClient(targetClientId);
            if (target == null) continue;                                    // 观战方 / 已无实体
            if (!IsInRadius(from, target.transform.position, radius)) continue;
            Tool.NetworkManager.SendChat(targetClientId, message);
        }
    }

    // 组装一条完整聊天消息：发送方名字、阵营、角色头像都只有服务器知道
    private SCChat BuildChat(int clientId, int channel, string content)
    {
        PlayerInfoList.TryGetValue(clientId, out var info);
        int campValue = CampValueOf(clientId);
        return new SCChat()
        {
            senderClientId = clientId,
            channel = channel,
            camp = campValue,
            characterIndex = CharacterIndexOf(clientId, campValue),
            name = info.name,
            content = content,
        };
    }

    private int CampValueOf(int clientId)
        => PlayerCamps.TryGetValue(clientId, out var camp) ? (int)camp : (int)PlayerCampType.Spectator;

    private int CharacterIndexOf(int clientId, int campValue)
    {
        if (!PlayerInfoList.TryGetValue(clientId, out var info)) return -1;
        if (campValue == 0 && info.attackCharacter.category == EntityCategory.Character_Attack) return info.attackCharacter.value;
        if (campValue == 1 && info.defenseCharacter.category == EntityCategory.Character_Defense) return info.defenseCharacter.value;
        return -1;
    }

    // 快捷消息报文不带角色，聊天条目按 clientId 反查头像下标
    public int CharacterIndexOfClient(int clientId) => CharacterIndexOf(clientId, CampValueOf(clientId));

    #region 快捷消息（玩家指令 → 同阵营人机）
    // 上行入口：服务器补齐发送者后按阵营广播；真人收回包，人机在服务器侧直接消费
    public void ReceiveQuickMessage(int clientId, CSQuickMessage message)
    {
        if (!BattleStarted) return;
        if (!PlayerCamps.TryGetValue(clientId, out var campType)) return;
        if (campType == PlayerCampType.Spectator) return;
        if (message.messageId < 0 || message.messageId > 7) return; // quick_message.csv 只有 0~7

        // 上报状况：数据由服务器按实体真实状态组装，不接受客户端自报；真人不上传状态，只报血量
        if (message.messageId == (int)QuickMessageType.ReportStatus)
        {
            var self = GetEntityOfClient(clientId);
            if (self == null || !self.Alive) return;
            float cap = self.attributes.MaxHealth;
            int healthPercent = cap > 0f ? Mathf.RoundToInt(self.attributes.Health / cap * 100f) : 0;
            ReportStatus(self, (int)EntityReportState.None, healthPercent);
            return;
        }

        BroadcastQuickMessage(new SCQuickMessage()
        {
            messageId = message.messageId,
            senderClientId = clientId,
            hasTargetPosition = message.hasTargetPosition,
            targetPosition = message.targetPosition,
        }, CampOf(clientId));
    }

    public void BroadcastQuickMessage(SCQuickMessage message, EntityCamp camp)
    {
        foreach (var pair in PlayerInfoList)
        {
            if (RobotClients.Contains(pair.Key)) continue;
            if (CampOf(pair.Key) != camp) continue;
            Tool.NetworkManager.SendQuickMessage(pair.Key, message);
        }

        foreach (var entity in EntityContainer.Entities)
        {
            if (entity == null || !entity.Alive || !entity.robotControlled) continue;
            if (entity.camp != camp) continue;
            // 人机忽略自己发的消息
            if (EntityOwnerClient.TryGetValue(entity.id, out var ownerClient) && ownerClient == message.senderClientId) continue;
            var player = entity as PlayerEntityData;
            if (player == null) continue;
            player.OnQuickMessage(message);
        }
    }

    // 人机/真人上报自身状况：只发同阵营真人，客户端按状态、血量、位置与周边敌人数本地拼聊天文本
    public void ReportStatus(EntityData entity, int state, int healthPercent)
    {
        if (entity == null) return;
        if (!EntityOwnerClient.TryGetValue(entity.id, out var clientId)) return;

        int campValue = CampValueOf(clientId);
        var report = new SCStateReport()
        {
            senderClientId = clientId,
            state = state,
            healthPercent = healthPercent,
            camp = campValue,
            characterIndex = CharacterIndexOf(clientId, campValue),
            position = entity.transform.position,
            nearbyEnemyCount = CountHostileAround(entity),
        };

        foreach (var pair in PlayerInfoList)
        {
            if (RobotClients.Contains(pair.Key)) continue;
            if (CampValueOf(pair.Key) != campValue) continue;
            Tool.NetworkManager.SendStateReport(pair.Key, report);
        }
    }

    private static readonly HashSet<int> s_hostileBuffer = new();

    // 周边敌对实体数量：含僵尸/雕像，珊瑚是资源不计；范围取配表「场景中角色的可见距离」（XZ）
    private static int CountHostileAround(EntityData self)
    {
        EntityCamp hostile = EntityCampUtil.HostileOf(self.type.category);
        EntityContainer.Entities.GetIdsInRange(self.transform.position,
            GlobalVariable.F(global_variablesEnum.entity_character_visiable_radius), s_hostileBuffer);

        int count = 0;
        foreach (int id in s_hostileBuffer)
        {
            if (!EntityContainer.Entities.TryGetObject(id, out var other) || other == null) continue;
            if (other.id == self.id || (hostile & other.camp) == 0 || !other.Alive) continue;
            if (other.type.category == EntityCategory.Coral) continue;
            count++;
        }
        return count;
    }
    #endregion

    #region 进攻方人机自动派点（服务器定时让进攻方人机主动压上）
    private Ticker robotAssaultTicker;

    // 每满一个间隔：周围没人的进攻方人机随机取 0~1，过半打最近的守护点，其余打最近的防御塔
    private void TickRobotAssaultOrders()
    {
        if (!robotAssaultTicker) return;

        foreach (var entity in EntityContainer.Entities)
        {
            if (entity == null || !entity.Alive || !entity.robotControlled) continue;
            if (entity.camp != EntityCamp.Attack) continue;
            var robot = entity as PlayerEntityData;
            if (robot == null) continue;

            // 周围有敌方角色（真人或人机）说明它正在交战，不打断；队友不阻断
            if (HasEnemyCharacterAround(entity, GlobalConfig.ai_assault_order_quiet_radius)) continue;

            if (!EntityOwnerClient.TryGetValue(entity.id, out var clientId))
            {
                Debug.LogError($"[战斗] 进攻方人机 {entity.id} 没有归属客户端，无法派进攻命令");
                continue;
            }

            Vector3 from = entity.transform.position;
            bool toGuard = UnityEngine.Random.value > GlobalConfig.ai_assault_order_ratio;
            var target = NearestAlive(toGuard ? EntityContainer.GuardStatues : EntityContainer.CrystalStatues, from);
            if (target == null) target = NearestAlive(toGuard ? EntityContainer.CrystalStatues : EntityContainer.GuardStatues, from);
            if (target == null)
            {
                Debug.LogWarning("[战斗] 进攻方人机派点跳过：场上没有存活的守护点与防御塔");
                continue;
            }

            robot.OnQuickMessage(new SCQuickMessage
            {
                messageId = (int)QuickMessageType.Attack,
                senderClientId = clientId,
                hasTargetPosition = true,
                targetPosition = target.transform.position,
            });
        }
    }

    private static readonly HashSet<int> s_characterIds = new();

    // 半径内是否有存活的敌方玩家角色（含人机；同阵营队友不算）
    private static bool HasEnemyCharacterAround(EntityData self, float radius)
    {
        EntityCamp hostile = EntityCampUtil.HostileOf(self.type.category);
        EntityContainer.Entities.GetIdsInRange(self.transform.position, radius, s_characterIds);
        bool found = false;
        foreach (int id in s_characterIds)
        {
            if (!EntityContainer.Entities.TryGetObject(id, out var other) || other == null) continue;
            if (other.id == self.id || !other.Alive) continue;
            if ((hostile & other.camp) == 0) continue;
            if (other.type.category != EntityCategory.Character_Attack && other.type.category != EntityCategory.Character_Defense) continue;
            found = true;
            break;
        }
        s_characterIds.Clear();
        return found;
    }

    // 最近存活实体：守护点与防御塔都只有个位数，直接遍历
    private static EntityData NearestAlive(ChunkSearcher<EntityData> source, Vector3 from)
    {
        EntityData best = null;
        float bestSqr = float.MaxValue;
        foreach (var candidate in source)
        {
            if (candidate == null || !candidate.Alive) continue;
            float sqr = (candidate.transform.position - from).sqrMagnitude;
            if (sqr >= bestSqr) continue;
            bestSqr = sqr;
            best = candidate;
        }
        return best;
    }
    #endregion

    // 进攻出生：每人各自在地图外围随机取点（距中心 > 0.3×边长）
    private Vector3 GetAttackSpawnPos()
    {
        if (TerrainSpawns.TrySampleAttackSpawn(out var pos)) return pos;
        Debug.LogError("[战斗] 进攻出生点采样失败：地图外围没有合法地面");
        return TerrainSpawns.MainGuard;
    }

    // 防守出生：在存活守护点（主 + 副）中随机挑一个，在其附近随机取点
    private Vector3 GetDefenseSpawnPos()
    {
        Vector3 center = PickDefenseSpawnCenter();
        if (TerrainSpawns.TrySampleDefenseSpawn(center, out var pos)) return pos;
        Debug.LogError($"[战斗] 防守出生点采样失败：守护点 {center} 附近没有合法地面");
        return center;
    }

    // 存活守护点里随机取圆心；全部被摧毁时退回主守护点
    private static readonly List<Vector3> s_guardCenterScratch = new();
    private static Vector3 PickDefenseSpawnCenter()
    {
        s_guardCenterScratch.Clear();
        foreach (var statue in EntityContainer.GuardStatues)
        {
            if (statue != null) s_guardCenterScratch.Add(statue.transform.position);
        }
        if (s_guardCenterScratch.Count == 0) return TerrainSpawns.MainGuard;
        Vector3 center = s_guardCenterScratch[UnityEngine.Random.Range(0, s_guardCenterScratch.Count)];
        s_guardCenterScratch.Clear();
        return center;
    }

    private void UpdateAI()
    {
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity != null) entity.TickAuto();
        }
    }

    public void AddStatueDamage(EntityData statue, float damage)
    {
        if (statue.type == EntityType.KingStatue) attackMainStatueDamage += damage;
        else attackSubStatueDamage += damage;
    }

    // 击杀数只认玩家角色：鲛人、水晶雕像、守护点等设施死亡不计分
    private void CountCampKill(EntityData entity)
    {
        if (entity.type.category == EntityCategory.Character_Attack) DefenseKills++;
        else if (entity.type.category == EntityCategory.Character_Defense) AttackKills++;
    }

    // 中立实体（中立雕像）被击杀：按最后一击所属阵营计入该阵营的中立击杀数
    public void AddNeutralKill(EntityCamp camp)
    {
        if (camp == EntityCamp.Attack) AttackNeutralKills++;
        else if (camp == EntityCamp.Defense) DefenseNeutralKills++;
    }

    public void UpdateKingStatueReduce()
    {
        int aliveOuter = 0;
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue != null && guardStatue.Alive && guardStatue.type != EntityType.KingStatue) aliveOuter++;
        }
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue != null && guardStatue.type == EntityType.KingStatue)
            {
                if (guardStatue.effectController == null) continue;
                // 分层减伤是独立计时类，重算前必须先移除，否则每次调用都会压一层
                guardStatue.effectController.RemoveEffect(EffectType.StatueReduce);
                if (aliveOuter > 0) guardStatue.effectController.AddEffect(EffectType.StatueReduce, aliveOuter, float.MaxValue);
            }
        }
    }

    public float StatueRemainingHealth()
    {
        float remaining = 0f;
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue != null) remaining += guardStatue.attributes.Health;
        }
        return remaining;
    }

    public float SubStatueRemainingHealth()
    {
        float remaining = 0f;
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue != null && guardStatue.type != EntityType.KingStatue) remaining += guardStatue.attributes.Health;
        }
        return remaining;
    }

    // 进攻分 =（副点伤害×系数 + 主点伤害×系数 + 击杀玩家数×分值）×(1 + 中立击杀系数×中立击杀数)
    public float AttackScore()
    {
        float damage = attackSubStatueDamage * GlobalVariable.F(global_variablesEnum.settlement_attack_sub_guard_damage_factor)
            + attackMainStatueDamage * GlobalVariable.F(global_variablesEnum.settlement_attack_main_guard_damage_factor);
        float kill = AttackKills * GlobalVariable.F(global_variablesEnum.settlement_attack_player_kill_score);
        return (damage + kill) * (1f + GlobalVariable.F(global_variablesEnum.settlement_attack_neutral_kill_factor) * AttackNeutralKills);
    }

    // 防守分 =（副点剩余×系数 + 主点剩余×系数 + 击杀玩家数×分值）×(1 + 中立击杀系数×中立击杀数)
    public float DefenseScore()
    {
        float health = SubStatueRemainingHealth() * GlobalVariable.F(global_variablesEnum.settlement_defense_sub_guard_health_factor)
            + MainStatueRemainingHealth() * GlobalVariable.F(global_variablesEnum.settlement_defense_main_guard_health_factor);
        float kill = DefenseKills * GlobalVariable.F(global_variablesEnum.settlement_defense_player_kill_score);
        return (health + kill) * (1f + GlobalVariable.F(global_variablesEnum.settlement_defense_neutral_kill_factor) * DefenseNeutralKills);
    }

    // 主守护点被摧毁由 DestroyEntity 直接判进攻方胜；其余触发时比较双方分数（2 = 防守方胜，1 = 进攻方胜）
    public int WinnerByScore()
    {
        return DefenseScore() >= AttackScore() ? 2 : 1;
    }

    // 结算展示用：主守护点剩余血量（实体已销毁即 0）
    public float MainStatueRemainingHealth()
    {
        EntityData king = FindMainStatue();
        if (king == null) return 0f;
        return king.attributes.Health;
    }

    private static EntityData FindMainStatue()
    {
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue != null && guardStatue.type == EntityType.KingStatue) return guardStatue;
        }
        return null;
    }

    #endregion

    #region 输入与技能接收（服务器）
    // AI 接管期间丢弃该客户端的操作：控制权在客户端 Lua，键盘/鼠标不应再影响实体
    private static bool KeyboardBlocked(EntityData entity)
    {
        return entity is PlayerEntityData player && player.aiControlled;
    }

    // 输入包共用的取实体口径：取不到（观战/已死）或 AI 接管期间一律丢弃
    private bool TryGetInputPlayer(int clientId, out PlayerEntityData player)
    {
        player = null;
        if (!PlayerEntityId.TryGetValue(clientId, out var entityId)) return false;
        if (!EntityContainer.Entities.TryGetObject(entityId, out var entity)) return false;
        if (KeyboardBlocked(entity)) return false;
        player = entity as PlayerEntityData;
        return player != null;
    }

    // 移动：参数是角色要转过头并前进的绝对朝向（客户端已按相机 yaw 合成好）；newPress = 由按下 WASD 触发
    public void ReceiveMove(int clientId, float yaw, bool newPress)
    {
        if (!TryGetInputPlayer(clientId, out var player)) return;
        player.MoveTo(yaw, newPress);
    }

    public void ReceiveMoveStop(int clientId)
    {
        if (!TryGetInputPlayer(clientId, out var player)) return;
        player.StopMove();
    }

    public void ReceiveJump(int clientId, bool down)
    {
        if (!TryGetInputPlayer(clientId, out var player)) return;
        player.SetJumpHeld(down);
    }

    public void ReceiveSlide(int clientId, bool down)
    {
        if (!TryGetInputPlayer(clientId, out var player)) return;
        player.SetSlideHeld(down);
    }

    // 技能键：客户端已经把按键解析成技能 id（技能栏在服务器），这里只转发给实体的统一收口
    public void ReceiveSkillCast(int clientId, int skillId)
    {
        if (skillId < 0) return;
        if (!PlayerEntityId.TryGetValue(clientId, out var entityId)) return;
        if (!EntityContainer.Entities.TryGetObject(entityId, out var entity)) return;
        // 连招窗口内的普攻：不重新起手，直接让动画接下一段（下一段的攻击帧自己会再结算一次技能）
        // 必须放在所有输入阻断之前：窗口内设 Combo 不接受任何阻断（AI 接管、攻击动画期间一律放行）
        if (skillId == GlobalConfig.unarmed_punch && entity.anim != null && entity.anim.AllowCombo)
        {
            entity.anim.DoCombo();
            return;
        }
        if (KeyboardBlocked(entity)) return; // AI 接管：技能由意图/释放指定技能驱动，不吃键盘
        if (entity.InputBlocked(InputBlockOp.Attack)) return; // 攻击动画等期间的输入阻断
        var result = entity.skillController.TryUseSkill(skillId);
        if (result != EntitySkillController.UseResult.Success) NotifySkillUseFailed(entity, result);
    }

    // 技能栏选择：没实体（观战/已死）忽略；无论校验是否通过都回推快照，客户端据此回到服务器口径
    public void ReceiveSkillSelection(int clientId, List<int> skillIds)
    {
        if (!PlayerEntityId.TryGetValue(clientId, out var entityId)) return;
        if (!EntityContainer.Entities.TryGetObject(entityId, out var entity)) return;
        if (entity.skillController == null) return;
        entity.skillController.TrySetSlotSkills(skillIds);
        SendSkillRecharge(clientId);
    }

    // 真人玩家的技能栏由服务器持有；AI 玩家与 NPC 没有栏外冻结，持有即恢复
    private void EnableSkillBar(int clientId, EntityData entity)
    {
        if (entity == null || entity.skillController == null) return;
        if (RobotClients.Contains(clientId)) return;
        entity.skillController.EnableSlotBar(entity.attributes.WeaponSlotCount);
    }
    #endregion

    #region 子弹（统一攻击实体）
    // 命中判定完全由 spec（特效命中球 + 配表倍率/间隔）驱动，不再有按寿命归一化的伤害窗口
    public void ShootBullet(EntityData shooter, AttackData attack, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletHitSpec spec = default)
    {
        AddBullet(attack, trajectory, lifeTime, spec);
    }
    #endregion

    #region 战斗规则
    public void StartBattle()
    {
        if (BattleStarted) return;

        // 清场上局残留实体（结算期间实体保留展示；须在 BattleStarted 置位前清除，避免触发国王雕像结束判定）
        var stale = new List<EntityData>();
        foreach (var e in EntityContainer.Entities)
        {
            if (e != null) stale.Add(e);
        }
        foreach (var e in stale)
        {
            DestroyEntity(e.id);
        }

        BattleStarted = true;
        BattleRemainTime = GlobalVariable.F(global_variablesEnum.battle_duration);
        ResetDayNight();
        // 先退订再订阅：防御方被动（教皇守护）靠昼夜翻转驱动，重复订阅会导致同一次入夜施加多遍
        DayNightFlipped -= OnDayNightFlipped;
        DayNightFlipped += OnDayNightFlipped;
        InitVision(); // 视野系统：小地图失联同样由昼夜翻转驱动

        // 开战重置：计分清零、子弹/移动/复活/重生状态清空（id 源已在加载阶段重置并预留，这里不再重置）
        if (reservedEntityIds.Count == 0) ResetEntityIdSource(); // 未经加载阶段直接开战（GM/调试）时兜底
        attackMainStatueDamage = 0f;
        attackSubStatueDamage = 0f;
        AttackKills = 0;
        AttackNeutralKills = 0;
        DefenseNeutralKills = 0;
        DefenseKills = 0;
        zombieCount = 0; // 上局僵尸已在上面的清场里逐个减过，这里再显式归零（与计分同一口径）
        ClearBattleState();

        // 周期任务计时器：开战重建，间隔取自配表（配表启动后不再变，构造时固化）
        syncTicker = new Ticker(GlobalConfig.entity_sync_interval_fast);
        detailsTicker = new Ticker(GlobalConfig.entity_sync_interval_details);
        dayNightTicker = new Ticker(GlobalConfig.daynight_sync_interval);
        minimapTicker = new Ticker(GlobalConfig.minimap_sync_interval);
        zombieSpawnTicker = new Ticker(GlobalVariable.F(global_variablesEnum.zombie_spawn_attempt_interval));
        coralSpawnTicker = new Ticker(GlobalVariable.F(global_variablesEnum.coral_spawn_attempt_interval));
        robotAssaultTicker = new Ticker(GlobalConfig.ai_assault_order_interval);
        robotAssaultTicker.Reset(); // 开战不派点，第一次要等满一个周期
        playerBoundsTicker = new Ticker(GlobalConfig.entity_bounds_check_interval_player);
        boundsCheckTicker = new Ticker(GlobalConfig.entity_bounds_check_interval);

        // 玩家实体重与 AI 同路径（AI clientId 为负虚拟 id），仅输入来源不同：真人 CSMove/CSJump/CSSlide、AI UpdateAI
        foreach (var pair in PlayerInfoList)
        {
            int clientId = pair.Key;
            var campType = PlayerCamps[clientId];
            if (campType == PlayerCampType.Spectator)
            {
                Tool.NetworkManager.SendPlayerEntity(clientId, -1); // 观战方无战斗实体
                continue;
            }
            bool isAttack = campType == PlayerCampType.Attack;
            EntityType characterType = isAttack ? pair.Value.attackCharacter : pair.Value.defenseCharacter;
            int level = GetPlayerLevel(clientId); // 局内等级：开局从 1 级起，复活沿用
            Vector3 spawnPos = isAttack ? GetAttackSpawnPos() : GetDefenseSpawnPos();
            EntityCamp camp = isAttack ? EntityCamp.Attack : EntityCamp.Defense;

            // 用加载阶段预分配的 id 生成，客户端拿到的 id 与加载期请求的表一致
            ushort entityId = PlayerEntityId.TryGetValue(clientId, out var reservedId)
                ? SpawnEntity(reservedId, characterType, level, spawnPos, camp)
                : SpawnEntity(characterType, level, spawnPos, camp);
            if (entityId == 0) continue; // 服务器模板缺失（SpawnEntity 已告警）
            PlayerEntityId[clientId] = entityId;
            EntityOwnerClient[entityId] = clientId;
            ApplyCharacterCardLevels(clientId, characterType); // 角色被动折算成初始词条层数
            Tool.NetworkManager.SendPlayerEntity(clientId, entityId);
            var spawned = GetEntity(entityId);
            if (spawned != null && spawned.skillController != null)
                SeedPlayerSkills(clientId, spawned.skillController.GetSkillIds()); // 玩家级持有表：角色自带技能打底
            EnableSkillBar(clientId, spawned); // 先建台账再下发技能，快照才有根戳可发

            // AI 接管 / 人机：置位后实体走对应的自动驱动分支（真人不受影响，输入仍来自网络）
            if (spawned is PlayerEntityData spawnedPlayer)
            {
                spawnedPlayer.robotControlled = RobotClients.Contains(clientId);
                spawnedPlayer.aiControlled = AiClients.Contains(clientId);
                if (spawnedPlayer.aiControlled && spawnedPlayer.AiRelay == null)
                    spawnedPlayer.AiRelay = new AiIntentRelay(spawnedPlayer, clientId); // 接管即挂行为状态机，不等第一条意图
            }
            Debug.Log($"{(RobotClients.Contains(clientId) ? "人机" : "真人")} {clientId} 出战：{characterType} camp={camp} id={entityId}");
        }

        // 对局世界：雕像×4 / 珊瑚 / 防御塔 / 中立雕像（位置来自地形点位 TerrainSpawns）
        SpawnBattleWorld();
        UpdateKingStatueReduce(); // 初始分层减伤 = 存活外围数 × 25%

        // 防守方开局被动（僵尸刷新等级 / 教皇守护）：须在双方实体生成后扫阵营；受击类被动在伤害结算里即时判定
        ApplyGlobalPassives();

        // 昼夜快照（周期时间 + 白天时长 + 晚上时长）：客户端据此自行推演，中途改时长会再补发
        Tool.NetworkManager.SendDayNightInfo(BuildSnapshot());

        BroadcastLobbyInfo();

        // 只告知已加载完成的客户端开战；未完成的观战方等它回执完成时补发
        LoadingStarted = false;
        reservedEntityIds.Clear();
        foreach (var clientId in loadedClients)
        {
            Tool.NetworkManager.SendBattleStart(clientId);
            SendSkillSyncFull(clientId); // 必须在 BattleStart 之后：客户端进战斗页会先清空技能表
        }
        SendProgressToAll(); // 开局把等级/幸运/待抽次数（全 0）推一次，客户端据此建立按钮列表
        Debug.Log($"战斗开始：真人 {PlayerInfoList.Count - RobotClients.Count}，人机 {RobotClients.Count}");
    }

    public void EndBattle(int gameState)
    {
        if (!BattleStarted) return;
        BattleStarted = false; // 先置 false：DestroyEntity 靠它判断要不要发雕像/水晶雕像摧毁事件
        Debug.Log($"对局结束：{gameState}");
        foreach (var clientId in PlayerInfoList.Keys)
        {
            HarvestByClient.TryGetValue(clientId, out float harvest);
            Tool.NetworkManager.SendScoreInfo(clientId, new SCScoreInfo()
            {
                gameState = gameState,
                attackScore = AttackScore(),
                defenseScore = DefenseScore(),
                killScore = DefenseKills,
                remainTime = Mathf.Max(0f, BattleRemainTime),
                attackKills = AttackKills,
                expGain = Mathf.RoundToInt(harvest), // 经验 = 采集量 = 对雕像造成的伤害量（策划案 17.3）
                guardStatueHealth = MainStatueRemainingHealth(),
            });
        }

        // 对局结束立即清理全部实体（结算展示由客户端自己保留/清除，服务器不再留残局实体）
        var stale = new List<EntityData>();
        foreach (var e in EntityContainer.Entities)
        {
            if (e != null) stale.Add(e);
        }
        foreach (var e in stale) DestroyEntity(e.id);
        PlayerEntityId.Clear();
        EntityOwnerClient.Clear();
        ResetCards(); // 词条是玩家级数据，跨局不保留
        zombieCount = 0;
        RemoveOfflineSessions(); // 掉线者不再有重连意义，回大厅前清出成员表
        readyByClient.Clear(); // 下一轮一律从「未准备」重新开始

        BroadcastLobbyInfo(); // battleStarted = false：客户端结算页关闭后回组队大厅准备下一轮
    }
    #endregion

    #region 帧循环（服务器权威推进）
    // 性能采样标签：在 Profiler 中把 ManagedUpdate 拆成独立项，按调用顺序逐个看耗时
    private const string kManagedUpdateProfilerTag = "BattleManager.ManagedUpdate";
    private const string kTimerTag = "BattleManager.Timer";
    private const string kDayNightTag = "BattleManager.DayNight";
    private const string kEntityUpdateTag = "BattleManager.EntityUpdate";
    private const string kTickMovementTag = "BattleManager.TickMovement";
    private const string kSyncTransformsTag = "BattleManager.SyncTransforms";
    private const string kTickBulletsTag = "BattleManager.TickBullets";
    private const string kTickReviveTag = "BattleManager.TickRevive";
    private const string kTickWorldRespawnTag = "BattleManager.TickWorldRespawn";
    private const string kBoundsCheckTag = "BattleManager.TickOutOfBounds";
    private const string kProcessKilledTag = "BattleManager.ProcessKilled";
    private const string kCampAliveCountTag = "BattleManager.CampAliveCount";
    private const string kDefensePointTag = "BattleManager.DefensePointProgress";
    private const string kUpdateAITag = "BattleManager.UpdateAI";
    private const string kRobotAssaultOrderTag = "BattleManager.RobotAssaultOrder";
    private const string kZombieRefreshTag = "BattleManager.ZombieRefresh";
    private const string kCoralSpawnTag = "BattleManager.CoralSpawn";
    private const string kSyncEntitiesTag = "BattleManager.SyncEntitiesToClients";
    private const string kSyncMinimapTag = "BattleManager.SyncMinimap";
    private const string kVisionStateTag = "BattleManager.VisionState";
    private const string kTransformDirtyTag = "BattleManager.TransformDirty";

    public override void ManagedUpdate()
    {
        Profiler.BeginSample(kManagedUpdateProfilerTag);

        // 昼夜：两端各自推进，服务器负责广播快照
        UpdateDayNight();

        if (!AtServer) AiPerceptionTick(); // 客户端：小地图缓存按超时剔除

        if (!AtServer || !BattleStarted) { Profiler.EndSample(); return; }

        // 计时
        Profiler.BeginSample(kTimerTag);
        if (BattleRemainTime > 0f)
        {
            BattleRemainTime -= UnityEngine.Time.deltaTime;
            if (BattleRemainTime <= 0f)
            {
                // 时间耗尽：按双方结算分判胜负（策划案 17.2）
                EndBattle(WinnerByScore());
                Profiler.EndSample(); // kTimerTag
                Profiler.EndSample(); // kManagedUpdateProfilerTag
                return;
            }
        }
        Profiler.EndSample(); // kTimerTag

        // 实体更新
        Profiler.BeginSample(kEntityUpdateTag);
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity != null) entity.OnUpdate();
        }
        Profiler.EndSample(); // kEntityUpdateTag

        // 战斗核心推进：权威移动（时间戳外推）/ 子弹容器 / 复活与珊瑚刷新
        Profiler.BeginSample(kTickMovementTag);
        TickMovement();
        Profiler.EndSample();
        Profiler.BeginSample(kBoundsCheckTag);
        TickOutOfBounds(); // 早于 SyncTransforms：传送当帧即同步给物理
        Profiler.EndSample();
        Profiler.BeginSample(kSyncTransformsTag);
        Physics.SyncTransforms(); //位移已由刚体积分，此处兜底按 transform 移动的对象（项目关闭了自动同步）
        Profiler.EndSample();
        Profiler.BeginSample(kTickBulletsTag);
        TickBullets();
        Profiler.EndSample();
        Profiler.BeginSample(kTickReviveTag);
        TickRevive();
        Profiler.EndSample();
        Profiler.BeginSample(kTickWorldRespawnTag);
        TickWorldRespawn();
        Profiler.EndSample();

        // 处理本帧死亡实体：回调 OnKilled 后立即销毁（不再播死亡动画）
        Profiler.BeginSample(kProcessKilledTag);
        if (EntityData.KilledList.Count > 0)
        {
            var killed = new List<EntityData>(EntityData.KilledList);
            foreach (var entity in killed)
            {
                entity.OnKilled();
                CountCampKill(entity);
                HandleDeath(entity);
            }
            EntityData.ClearKilled();
        }
        Profiler.EndSample(); // kProcessKilledTag

        Profiler.BeginSample(kCampAliveCountTag);
        TickCampAliveCount();
        Profiler.EndSample();

        Profiler.BeginSample(kDefensePointTag);
        TickDefensePointProgress(); // 被毁雕像的进度已在 DestroyEntity 里定死 100，这里只推进存活雕像
        Profiler.EndSample();

        // AI 行为（各实体在内部按 id 错峰，见 UpdateAI）
        Profiler.BeginSample(kUpdateAITag);
        UpdateAI();
        Profiler.EndSample();

        // 进攻方人机定时派点：避免人机一直在外围乱逛不进攻
        Profiler.BeginSample(kRobotAssaultOrderTag);
        TickRobotAssaultOrders();
        Profiler.EndSample();

        // 静默回收（鲛人卡死等）：AI 一拍内产出，本帧就移除并同步客户端，不算死亡、不结算掉落
        if (EntityData.DiscardedList.Count > 0)
        {
            foreach (var entity in EntityData.DiscardedList)
            {
                if (entity != null) DestroyEntity(entity.id);
            }
            EntityData.ClearDiscarded();
        }

        // 僵尸刷新（策划案第九章）：每满一个间隔做一批尝试，数量 = 昼夜速率 × 进攻方人数
        Profiler.BeginSample(kZombieRefreshTag);
        TickZombieSpawn();
        Profiler.EndSample(); // kZombieRefreshTag

        // 珊瑚邻近生成：每满一个间隔做一批尝试，尝试次数按场上珊瑚数在配表快/慢两档间切换
        Profiler.BeginSample(kCoralSpawnTag);
        TickCoralSpawn();
        Profiler.EndSample();

        // 视野锚点逐帧刷新（观战跟随 / 死亡冻结），供实体同步与小地图使用
        Profiler.BeginSample(kVisionStateTag);
        TickVisionStates();
        Profiler.EndSample();

        // 位姿脏标记（必须在本帧移动/物理同步之后判定）：与上次下发位姿差超阈值才置脏，无脏的快轮整包不下发
        Profiler.BeginSample(kTransformDirtyTag);
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity != null) entity.CheckTransformDirty();
        }
        Profiler.EndSample();

        // 同步实体表现给客户端（0.02s 节流；详细数据 0.2s；仅视野内的实体，见 SyncEntitiesToClients）
        Profiler.BeginSample(kSyncEntitiesTag);
        if (syncTicker) SyncEntitiesToClients(detailsTicker.Tick());
        Profiler.EndSample(); // kSyncEntitiesTag

        // 小地图同步（阵营共享视野，独立节流，见 GlobalConfig.minimap_sync_interval）
        Profiler.BeginSample(kSyncMinimapTag);
        if (minimapTicker) SyncMinimapToClients();
        Profiler.EndSample(); // kSyncMinimapTag
        Profiler.EndSample(); // kManagedUpdateProfilerTag
    }

    private void SyncEntitiesToClients(bool fullRound)
    {
        foreach (var clientId in PlayerInfoList.Keys) SyncEntitiesToClient(clientId, fullRound);

        // 本轮至少被一个客户端可见的实体：清 health/buff/skill 脏标记（无人可见则保留，等下次可见随完整同步下发）
        foreach (var id in s_dirtySweep)
        {
            var e = GetEntity(id);
            if (e == null) continue;
            e.healthDirty = false;
            e.buffDirty = false;
            e.skillDirty = false;
            // 位姿待发次数每轮只 +1（与客户端数量无关）；基准只在真的发出去后推进，否则慢速移动永远攒不满阈值
            if (fullRound || e.transformDirty < 2) e.OnTransformSent();
        }
        s_dirtySweep.Clear();

        // 脏标记是实体级的（与客户端无关），全部客户端处理完再统一清除
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity != null && entity.anim != null) entity.anim.ClearAnimSyncDirty();
        }
    }

    // 单客户端补发：观战切换时立刻刷出目标视野，不等下一轮同步；不碰实体级脏标记，交给下一次整轮统一清理
    private void SyncEntitiesToClient(int clientId, bool fullRound)
    {
        // 未完成加载的客户端不发战斗表现数据：其可见集合保持为空，回执后第一轮自然按首见全量补上
        if (!loadedClients.Contains(clientId)) return;
        // 视野锚点（存活跟随自己 / 观战跟随目标 / 死亡冻结在死亡位置），死亡不再跳过：按冻结位置继续下发
        if (!visionStates.TryGetValue(clientId, out _)) return;
        // 上一轮可见集合：用于判断"刚进入视野"，新实体必须整包下发（客户端要靠持久参数才能摆对状态）
        visibleByClient.TryGetValue(clientId, out var prevSeen);
        BeginClientVisibility(clientId);
        foreach (var entity in EntityContainer.Entities)
        {
            if (entity == null) continue;
            if (!CanSeeDisplayFromState(clientId, entity)) continue;
            MarkClientVisible(entity.id); // 后面可能跳过下发，但可见集合仍要标上，否则会被当成离屏而回 remove 包
            bool firstSeen = prevSeen == null || !prevSeen.Contains(entity.id);
            bool full = fullRound || firstSeen;
            var info = entity.GetDisplayInfo(full);
            // 位姿/血量/buff 全无变化就不发整包；全量轮 full 恒真，兼作静止实体的心跳
            if (info.hasTransform || info.hasHealth || info.hasBuffs)
            {
                FillDisplayVelocity(entity, ref info);
                Tool.NetworkManager.SendEntityDisplay(clientId, info);
            }
            // 动画包独立于表现包：表现包被跳过时仍要发，否则本轮末尾清掉 AnimSyncDirty 后这次状态变化就永久丢了
            if (entity.anim != null && (entity.anim.AnimSyncDirty || firstSeen)) SendEntityAnim(clientId, entity);
        }
        EndClientVisibility(clientId);
    }

    private void SendEntityAnim(int clientId, EntityData entity)
    {
        entity.anim.GetDisplayAnim(out int animId, out float frame);
        Tool.NetworkManager.SendEntityAnim(clientId, new SCEntityAnimInfo()
        {
            entityId = entity.id,
            animId = animId,
            animFrame = frame,
            animParams = entity.anim.GetParamPack(),
            moveSpeedScale = entity.anim.MoveSpeedScale,
            paused = entity.anim.Paused,
        });
    }

    private void FillDisplayVelocity(EntityData entity, ref SCEntityDisplayInfo info)
    {
        if (!info.hasTransform) return; // 不带位姿的包也不带速度，客户端不会拿它外插
        // 位置由物理积分，必须取刚体实际速度：用"意图速度"外插会与权威位置持续漂移；无刚体（不可移动单位）即无速度
        info.velocity = entity.rb != null ? entity.rb.velocity : Vector3.zero;
        info.yawSpeed = entity.YawSpeed;
    }
    #endregion
}
