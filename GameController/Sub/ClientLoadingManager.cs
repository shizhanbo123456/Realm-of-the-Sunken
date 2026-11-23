using System.Collections.Generic;
using Ros.Config;
using Ros.Transport;
using UnityEngine;

// 客户端加载管理器：切加载界面 → 逐步请求开战数据 → 回执加载完成 → 收到「开始战斗」后切页。
// 战斗期的玩家名字与玩家↔实体关系全部来自这里，不再读组队期缓存。
public class ClientLoadingManager : ClientSubManager
{
    private enum Step
    {
        Idle,           // 未开始或已结束
        Names,          // 全量名字表（分批拉，观战方也在里面）
        Units,          // 参战单位表（真人 + 人机、各阵营混排，分批拉）
        BattleAssets,   // 僵尸/精英/雕像/珊瑚预加载
    }

    private readonly List<LoadingUnitEntry> units = new(); // 参战单位：真人玩家 + 人机
    private readonly Dictionary<int, string> names = new(); // 玩家令牌 → 名字（加载期全量表，含观战方）
    private readonly Dictionary<ushort, int> entityOwners = new(); // 实体 id → 玩家令牌（名牌反查）
    private readonly Dictionary<int, int> playerEntities = new(); // 玩家令牌 → 实体 id（-1 = 无实体）

    private Step step = Step.Idle;
    private bool terrainReady;       // 地形已生成（素材表要异步加载，不能假定在 Prepared 之前完成）
    private bool preparedReceived;
    private int expectedNames;       // 元数据给的名字总条数：收满才进下一步
    private int expectedUnits;       // 元数据给的参战单位总条数：收满才进下一步

    public static ClientLoadingManager Current =>
        Tool.ClientLogicManager != null ? Tool.ClientLogicManager.Loading : null;

    public bool HasSession { get; private set; }
    public bool IsResync { get; private set; } // 本次会话是断线重连的补发（技能库存按零起算）
    public EntityCamp Camp { get; private set; }
    public ushort OwnEntityId { get; private set; }
    public EntityType CharacterType { get; private set; } // 本局使用的角色（结算算角色经验用）
    public bool IsSpectator => HasSession && Camp == EntityCamp.None;

    protected override void BindEvents()
    {
        EventManager.AddEvent(ClientEvent.OnExitWorld, Clear);
        EventManager.AddEvent(ClientEvent.OnRestartGame, Clear);
        EventManager.AddEvent<SCReviveInfo>(ClientEvent.OnReviveProgressUpdate, OnReviveProgress);
    }

    protected override void UnbindEvents()
    {
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, Clear);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, Clear);
        EventManager.RemoveEvent<SCReviveInfo>(ClientEvent.OnReviveProgressUpdate, OnReviveProgress);
    }

    #region//加载流程
    // 服务器宣告进入加载阶段：切加载界面（此时还没有实体 id，等 Prepared）
    public void OnLoadingStart(SCLoadingStart info)
    {
        ClearData();
        IsResync = info.resync;
        if (info.resync)
        {
            // 断线重连：本地实体视图全部作废，等服务器按「首见全量」重发；冷却留在服务器，随技能快照回来
            Tool.ClientLogicManager.EntityPlayers.ClearAll();
        }
        HasSession = true;
        Camp = info.camp;
        OwnEntityId = 0;
        step = Step.Idle;
        terrainReady = false;
        preparedReceived = false;
        if (Tool.UIManager != null) Tool.UIManager.ShowLoading();
        GenerateTerrain();
    }

    // 服务器预处理完成（实体 id 已预分配 + 拉取清单）：地形就绪后按清单逐个来拉
    public void OnLoadingPrepared(SCLoadingPrepared info)
    {
        OwnEntityId = info.ownEntityId;
        CharacterType = info.characterType;
        expectedNames = info.nameCount;
        expectedUnits = info.unitCount;
        NetworkManager.SyncServerTime(info.serverTime); // 服务器时间基准，顺带校准本地时钟
        preparedReceived = true;
        TryStartSteps();
    }

    private void TryStartSteps()
    {
        if (step != Step.Idle || !terrainReady || !preparedReceived) return;
        step = Step.Names;
        if (Tool.UIManager != null) Tool.UIManager.SetLoadingProgress(0f, MessageVariable.V(message_variablesEnum.loading_text_prepare));
        RequestNextNames();
    }

    // 名字表按偏移分批拉：只按已收到的条数推进，漏了哪批下次自然重拉同一偏移
    private void RequestNextNames()
    {
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendLoadingNamesRequest(names.Count);
    }

    // 收到一批名字：没拉满就按新偏移继续要，收满才进单位表
    public void OnPlayerNames(SCLoadingPlayerNames chunk)
    {
        if (step != Step.Names || chunk.list == null) return;
        if (chunk.list.Count == 0)
        {
            // 快照条数与元数据由服务器同一处算出，为空说明两边口径已不一致，继续拉只会空转
            Debug.LogError($"[加载] 名字表提前结束：已收 {names.Count} / 元数据 {expectedNames}");
            StartUnitStep();
            return;
        }
        foreach (var entry in chunk.list) names[entry.clientId] = entry.name;
        if (names.Count < expectedNames) { RequestNextNames(); return; }
        StartUnitStep();
    }

    private void StartUnitStep()
    {
        step = Step.Units;
        if (Tool.UIManager != null) Tool.UIManager.SetLoadingProgress(1f / 4f, MessageVariable.V(message_variablesEnum.loading_text_prepare));
        RequestNextUnits();
    }

    // 单位表同一套偏移拉法：收多少推进多少，漏批下次重新要同一偏移
    private void RequestNextUnits()
    {
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendLoadingUnitRequest(units.Count);
    }

    public void OnUnitResponse(SCLoadingUnitResponse chunk)
    {
        if (step != Step.Units || chunk.list == null) return;
        if (chunk.list.Count == 0)
        {
            Debug.LogError($"[加载] 单位表提前结束：已收 {units.Count} / 元数据 {expectedUnits}");
            StartBattleAssets();
            return;
        }
        Merge(chunk.list);
        if (units.Count < expectedUnits) { RequestNextUnits(); return; }
        StartBattleAssets();
    }

    private void StartBattleAssets()
    {
        step = Step.BattleAssets;
        if (Tool.UIManager != null) Tool.UIManager.SetLoadingProgress(3f / 4f, MessageVariable.V(message_variablesEnum.loading_text_battleAssets));
        if (Tool.ResourceManager == null)
        {
            Debug.LogError("[加载] 缺少 ResourceManager：僵尸与珊瑚未预加载");
            FinishLoading();
            return;
        }
        Tool.ResourceManager.LoadBattleNecessary(FinishLoading);
    }

    private void FinishLoading()
    {
        step = Step.Idle;
        if (Tool.UIManager != null) Tool.UIManager.SetLoadingProgress(1f, MessageVariable.V(message_variablesEnum.loading_text_done));
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendLoadingComplete();
    }

    // 服务器允许进入战斗：收起加载界面，切页交给 UIManager（监听 OnBattleStart）
    public void OnBattleStart()
    {
        if (Tool.UIManager != null) Tool.UIManager.HideLoading();
        EventManager.TrigEvent(ClientEvent.OnBattleStart, (int)Camp);
    }
    #endregion

    #region//数据查询
    // 战斗期名字的唯一读取口：名字表查不到才回退缺省名
    public string GetName(int clientId)
    {
        if (names.TryGetValue(clientId, out var name) && !string.IsNullOrEmpty(name)) return name;
        return MessageVariable.F(message_variablesEnum.default_playerName, clientId);
    }

    public string GetNameByEntity(ushort entityId)
    {
        return entityOwners.TryGetValue(entityId, out var clientId) ? GetName(clientId) : MessageVariable.V(message_variablesEnum.default_unknownPlayer);
    }

    // clientId → 当前实体 id；-1 = 无实体（死亡 / 观战 / 离场）
    public int GetEntityId(int clientId)
    {
        return playerEntities.TryGetValue(clientId, out var entityId) ? entityId : -1;
    }

    // 所有参战单位（真人 + AI），供死亡观战/纯观战的目标列表使用
    public List<LoadingUnitEntry> GetCombatants() => units;
    #endregion

    #region//Local
    // 地形一次性生成在加载界面：之后由 Culler 按主相机位置取还区块，离开战斗页再销毁
    // 素材表走 Addressables，生成完成才放行后面的加载步骤
    private void GenerateTerrain()
    {
        if (Tool.ModularTerrainGenerator == null)
        {
            Debug.LogError("[地形] 场景未挂 ModularTerrainGenerator：进战斗不会生成地形");
            terrainReady = true;
            TryStartSteps();
            return;
        }

        if (Tool.UIManager != null) Tool.UIManager.SetLoadingProgress(0f, MessageVariable.V(message_variablesEnum.loading_text_terrain));
        Tool.ModularTerrainGenerator.Generate(OnTerrainGenerated);
    }

    private void OnTerrainGenerated()
    {
        terrainReady = true;
        TryStartSteps();
    }

    // 单位表只填实体关系，名字由 SCLoadingPlayerNames 单独下发
    private void Merge(List<LoadingUnitEntry> list)
    {
        if (list == null) return;
        foreach (var entry in list)
        {
            units.Add(entry);
            entityOwners[entry.entityId] = entry.clientId;
            playerEntities[entry.clientId] = entry.entityId;
        }
    }

    // 玩家 → 实体关系由网络层直连转入：开战前的批量下发与界面创建存在时差，走事件会整批丢包
    public void OnPlayerEntity(SCPlayerEntity info)
    {
        if (!HasSession) return;
        BindEntity(info.clientId, info.entityId);
    }

    // 复活包单播到本人且必达，故本地实体 id 一并由它刷新（SCPlayerEntity 的广播不会回到房主自己的客户端）
    private void OnReviveProgress(SCReviveInfo info)
    {
        if (!info.ready || info.entityId == 0) return;
        BindEntity(NetworkManager.LocalToken, info.entityId);
    }

    // clientId → 当前实体 id（-1 = 无实体）；本地玩家同时刷新 OwnEntityId
    private void BindEntity(int clientId, int entityId)
    {
        // 复活换新实体 id：清掉该玩家的旧关系，避免旧 id 仍能反查到人
        if (playerEntities.TryGetValue(clientId, out var old) && old > 0
            && entityOwners.TryGetValue((ushort)old, out var oldOwner) && oldOwner == clientId)
            entityOwners.Remove((ushort)old);

        playerEntities[clientId] = entityId;
        if (entityId > 0) entityOwners[(ushort)entityId] = clientId;

        if (clientId == NetworkManager.LocalToken)
            OwnEntityId = entityId > 0 ? (ushort)entityId : (ushort)0;
    }

    private void Clear()
    {
        ClearData();
        IsResync = false;
        HasSession = false;
        Camp = EntityCamp.None;
        OwnEntityId = 0;
        if (Tool.UIManager != null) Tool.UIManager.HideLoading();
    }

    private void ClearData()
    {
        units.Clear();
        names.Clear();
        entityOwners.Clear();
        playerEntities.Clear();
        expectedNames = 0;
        expectedUnits = 0;
        step = Step.Idle;
    }
    #endregion
}
