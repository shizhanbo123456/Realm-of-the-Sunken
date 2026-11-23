using System.Collections.Generic;
using Ros.Config;
using Ros.Skill;
using Ros.Transport;
using UnityEngine;

// 人机玩家状态机：15 个决策状态 + 快捷消息任务系统（设计见 Doc/08-游戏AI逻辑），执行交给公共行为层
public partial class RobotAi : DecisionMachine, IBehaviorHost
{
    // 任务来源消息（锚点由"是否携带位置"决定静态/动态）
    protected struct TaskTarget
    {
        public bool hasPosition;
        public Vector3 position;
        public EntityData sender;
    }

    public readonly PlayerEntityData Owner;

    private readonly RobotSkillBar skillBar;

    private readonly BehaviorArbiter arbiter;

    private readonly SearchState searchState;
    private readonly SearchCombatState searchCombatState;
    private readonly ChaseState chaseState;
    private readonly CombatDecision combatState;
    private readonly EscortApproachState escortApproachState;
    private readonly EscortState escortState;
    private readonly CombatDecision escortCombatState;
    private readonly DangerEvadeState dangerEvadeState;
    private readonly DangerClearState dangerClearState;
    private readonly CombatDecision dangerCombatState;
    private readonly RallyApproachState rallyApproachState;
    private readonly RallyWaitState rallyWaitState;
    private readonly CombatDecision rallyGuardState;
    private readonly AssaultState assaultState;
    private readonly CombatDecision assaultCombatState;

    // 任务槽：单槽，后到覆盖
    private bool hasPending;
    private QuickMessageType pendingType;
    private TaskTarget pendingTarget;

    // 卡住检测：一段时间没有位移就换目标
    private Vector3 stuckSamplePos;
    private float stuckSampleTime;

    public RobotAi(PlayerEntityData owner)
    {
        Owner = owner;
        skillBar = new RobotSkillBar(owner, this);
        ThinkInterval = GlobalConfig.ai_think_interval;

        searchState = new SearchState(this);
        searchCombatState = new SearchCombatState(this);
        chaseState = new ChaseState(this);
        combatState = new CombatDecision(this);
        escortApproachState = new EscortApproachState(this);
        escortState = new EscortState(this);
        escortCombatState = new CombatDecision(this);
        dangerEvadeState = new DangerEvadeState(this);
        dangerClearState = new DangerClearState(this);
        dangerCombatState = new CombatDecision(this);
        rallyApproachState = new RallyApproachState(this);
        rallyWaitState = new RallyWaitState(this);
        rallyGuardState = new CombatDecision(this);
        assaultState = new AssaultState(this);
        assaultCombatState = new CombatDecision(this);

        arbiter = new BehaviorArbiter(this);

        // 战斗态：撤离血线 / 活动范围 / 撤离去向 按战斗策略表配；脱战回到由 Bind 时传入
        combatState.Setup(GlobalConfig.ai_combat_retreat_ratio, 0f, dangerEvadeState, EntityReportState.Combat);
        escortCombatState.Setup(GlobalConfig.ai_escort_retreat_ratio, GlobalConfig.ai_escort_combat_radius * 1.5f, dangerEvadeState, EntityReportState.EscortCombat);
        dangerCombatState.Setup(GlobalConfig.ai_combat_retreat_ratio, GlobalConfig.ai_danger_clear_radius * 1.5f, dangerEvadeState, EntityReportState.DangerClearCombat);
        rallyGuardState.Setup(GlobalConfig.ai_rally_retreat_ratio, GlobalConfig.ai_guard_alert_radius, rallyApproachState, EntityReportState.RallyGuard);
        assaultCombatState.Setup(GlobalConfig.ai_combat_retreat_ratio, GlobalConfig.ai_assault_combat_activity, dangerEvadeState, EntityReportState.AssaultCombat);

        stuckSamplePos = owner.transform.position;
        stuckSampleTime = Time.time;
        Initialize(searchState, owner.AutoStaggerPhase);
    }

    // 决策按 ThinkInterval 节流，行为层每帧仲裁
    public override void Tick()
    {
        if (!Owner.Alive) return;
        base.Tick();
        arbiter.Tick(Time.deltaTime);
    }

    protected override EntityData MachineOwner => Owner;

    protected override void OnThink()
    {
        skillBar.Rebuild(); // 持有表可能因掉落/复活变化，重排一次即"获得技能重新入栏"
        UpdateStuckSample();
    }

    #region//Local
    protected float HealthRatio
    {
        get
        {
            float cap = Owner.attributes.MaxHealth;
            return cap > 0f ? Owner.attributes.Health / cap : 0f;
        }
    }

    protected float ViewDistance => Owner.attributes.ViewDistance;

    // 静止一段时间说明目的地走不到
    private void UpdateStuckSample()
    {
        // 执行层已判出「有方向却没位移」（贴住墙角被几何挡住）：立刻放弃，不必等满采样周期
        if (Owner.NavStalled)
        {
            Owner.StopMoving(); // 同时清掉卡死信号，下一拍从零重新累计
            NotifyStuck();
            stuckSampleTime = Time.time;
            stuckSamplePos = Owner.transform.position;
            return;
        }

        if (Time.time - stuckSampleTime < GlobalConfig.ai_unreachable_pause) return;
        Vector3 delta = Owner.transform.position - stuckSamplePos;
        delta.y = 0f;
        stuckSampleTime = Time.time;
        stuckSamplePos = Owner.transform.position;
        if (delta.magnitude >= GlobalConfig.ai_stuck_min_move) return;

        // 丢掉走不到的目的地，交给状态换目标，避免一直对着同一处撞
        Owner.StopMoving();
        NotifyStuck();
    }

    private void NotifyStuck() => Current?.OnStuck();

    // 不可达的目标忽略一段时间，避免放弃后下一拍又锁回同一个
    private EntityData ignoredTarget;
    private float ignoreTargetUntil;

    protected void IgnoreTarget(EntityData target)
    {
        if (target == null) return;
        ignoredTarget = target;
        ignoreTargetUntil = Time.time + GlobalConfig.ai_ignore_unreachable_time;
    }

    private bool IsIgnored(EntityData entity)
        => entity != null && entity == ignoredTarget && Time.time < ignoreTargetUntil;

    protected void FaceTarget(EntityData target)
    {
        if (target == null) return;
        Owner.SetRobotFacePoint(target.transform.position);
    }

    private float comboRetryTime; // 上次续段时刻（普攻按 0.1s 节拍连段）

    // 站定攻击：连招窗口内续接普攻下一段（等同真人连点普攻），否则技能优先、全不可用退普攻
    protected bool TryAttack(EntityData target)
    {
        if (skillBar.PunchChain && Owner.anim != null && Owner.anim.AllowCombo)
        {
            if (Time.time < comboRetryTime) return false;
            comboRetryTime = Time.time + GlobalConfig.attack_retry_interval;
            Owner.anim.DoCombo();
            return true;
        }
        if (Owner.InputBlocked(InputBlockOp.Attack)) return false;
        return skillBar.TryCastAny(target);
    }

    protected EntityData NearestEnemy(bool includeCoral)
    {
        // 索敌半径取「视野」与「追击上限 - 余量」的较小者：等于上限时刚索到就会超限放弃，搜索/追击每拍互踢
        float radius = Mathf.Min(ViewDistance, GlobalConfig.ai_chase_limit - GlobalConfig.ai_acquire_margin);
        return NearestEnemyAround(Owner.transform.position, radius, includeCoral);
    }

    protected EntityData NearestEnemyAround(Vector3 center, float radius, bool includeCoral)
    {
        var hostile = EntityCampUtil.HostileOf(Owner.type.category);
        s_ids.Clear();
        BattleManager.EntityContainer.Entities.GetIdsInRange(center, radius, s_ids);

        EntityData best = null;
        float bestSqr = float.MaxValue;
        foreach (var id in s_ids)
        {
            if (!BattleManager.EntityContainer.Entities.TryGetObject(id, out var e)) continue;
            if (e == null || !e.Alive || e.id == Owner.id) continue;
            if (IsIgnored(e)) continue;
            if ((hostile & e.camp) == 0) continue;
            bool isCoral = e.type.category == EntityCategory.Coral;
            if (isCoral != includeCoral) continue;
            float d = (e.transform.position - center).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = e; }
        }
        s_ids.Clear();
        return best;
    }

    // 高威胁 = 玩家/人机角色：移动类任务遇到时要先打，僵尸/塔/珊瑚这类低威胁继续听指令
    private static bool IsHighThreat(EntityData e)
    {
        return e.type.category == EntityCategory.Character_Attack || e.type.category == EntityCategory.Character_Defense;
    }

    protected EntityData NearestHighThreat()
    {
        float radius = Mathf.Min(ViewDistance, GlobalConfig.ai_chase_limit - GlobalConfig.ai_acquire_margin);
        return NearestHighThreatAround(Owner.transform.position, radius);
    }

    protected EntityData NearestHighThreatAround(Vector3 center, float radius)
    {
        var hostile = EntityCampUtil.HostileOf(Owner.type.category);
        s_ids.Clear();
        BattleManager.EntityContainer.Entities.GetIdsInRange(center, radius, s_ids);

        EntityData best = null;
        float bestSqr = float.MaxValue;
        foreach (var id in s_ids)
        {
            if (!BattleManager.EntityContainer.Entities.TryGetObject(id, out var e)) continue;
            if (e == null || !e.Alive || e.id == Owner.id) continue;
            if (IsIgnored(e)) continue;
            if ((hostile & e.camp) == 0) continue;
            if (!IsHighThreat(e)) continue;
            float d = (e.transform.position - center).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = e; }
        }
        s_ids.Clear();
        return best;
    }

    private static readonly HashSet<int> s_ids = new();

    // 防守方：定时扫描己方守护点，取离自己最近的"正在打守护点"的敌人
    protected EntityData FindGuardingEnemy()
    {
        EntityData best = null;
        float bestSqr = float.MaxValue;
        foreach (var statue in BattleManager.EntityContainer.GuardStatues)
        {
            if (statue == null || !statue.Alive || statue.camp != Owner.camp) continue;
            var enemy = NearestEnemyAround(statue.transform.position, GlobalConfig.ai_guard_alert_radius, false);
            if (enemy == null) continue;
            float d = (enemy.transform.position - Owner.transform.position).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; best = enemy; }
        }
        return best;
    }

    protected float FlatDistance(Vector3 point)
    {
        Vector3 d = point - Owner.transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    protected float FlatDistanceTo(EntityData target) => FlatDistance(target.transform.position);

    // 队伍呼叫：同阵营、在半径内的其他 AI 一起追这个敌人
    protected void GroupCall(EntityData target, float radius)
    {
        if (target == null) return;
        float sqr = radius * radius;
        foreach (var e in BattleManager.EntityContainer.Entities)
        {
            if (e == null || e == Owner || !e.Alive || !e.robotControlled) continue;
            if (e.camp != Owner.camp) continue;
            var player = e as PlayerEntityData;
            if (player == null) continue;
            if ((e.transform.position - Owner.transform.position).sqrMagnitude > sqr) continue;
            player.RequestRobotChase(target);
        }
    }

    // 中立单位（争夺中立的目标）
    protected EntityData FindNeutralStatue()
    {
        foreach (var e in BattleManager.EntityContainer.Entities)
        {
            if (e == null || !e.Alive) continue;
            if (e.type.category == EntityCategory.NeutralStatue) return e;
        }
        return null;
    }

    protected void ReportStatus()
    {
        if (Tool.BattleManager == null) return;
        Tool.BattleManager.ReportStatus(Owner, (int)Current.ReportState, Mathf.RoundToInt(HealthRatio * 100f));
    }

    // 接敌距离 = 栏内此刻真能放的技能里最远的射程；全场技能只在还有库存时拉近距离，放完自动退回普攻射程
    protected float AttackRange
    {
        get
        {
            var slots = skillBar.Slots;
            float best = 0f;
            for (int i = 0; i < slots.Count; i++)
            {
                if (!Owner.skillController.IsSkillReady(slots[i])) continue; // 冷却中/没库存的不参与，避免站着等它
                if (!SkillManager.TryGet(slots[i], out var skill)) continue;
                if (skill.CastRange > best) best = skill.CastRange;
            }
            return best > 0f ? best : SkillConfig.Get(GlobalConfig.unarmed_punch).castRange;
        }
    }

    private CombatDecision ResolveCombat(DecisionState returnState)
    {
        if (returnState == dangerClearState) return dangerCombatState;
        if (returnState == assaultState) return assaultCombatState;
        return combatState;
    }

    // 静态锚点用快照；动态锚点读发送者当前位置，发送者失效则任务作废
    protected bool TryResolveAnchor(TaskTarget target, out Vector3 point)
    {
        point = default;
        if (target.hasPosition) { point = target.position; return true; }
        if (target.sender == null || !target.sender.Alive) return false;
        point = target.sender.transform.position;
        return true;
    }
    #endregion

    #region//消息处理
    public void OnQuickMessage(SCQuickMessage message)
    {
        if (!Owner.Alive) return;
        var type = (QuickMessageType)message.messageId;

        if (type == QuickMessageType.ReportStatus) return; // AI 自己发的，不处理
        if (type == QuickMessageType.RequestReport) { ReportStatus(); return; }
        if (type == QuickMessageType.ReleaseSignal) { CancelAll(); return; }
        if (type == QuickMessageType.Danger) { HandleDanger(message); return; }
        if (type == QuickMessageType.Attack && !CanAcceptAttackMessage()) return;

        var target = new TaskTarget
        {
            hasPosition = message.hasTargetPosition,
            position = message.targetPosition,
            sender = ResolveSender(message.senderClientId),
        };

        // 战斗态与危险躲避：只入槽，脱战后执行
        if (Current != null && Current.BlocksTaskMessages) { StorePending(type, target); return; }
        SwitchTo(BuildTaskState(type, target, searchState, false));
    }

    private EntityData ResolveSender(int clientId)
    {
        return Tool.BattleManager != null ? Tool.BattleManager.GetSenderEntity(clientId) : null;
    }

    private void HandleDanger(SCQuickMessage message)
    {
        Vector3 point = message.hasTargetPosition
            ? message.targetPosition
            : (ResolveSender(message.senderClientId) != null ? ResolveSender(message.senderClientId).transform.position : Owner.transform.position);

        bool nearby = FlatDistance(point) <= GlobalConfig.ai_danger_response_distance;
        if (nearby && HealthRatio > GlobalConfig.ai_danger_response_ratio)
            SwitchTo(dangerClearState.Bind(point));
        else
            SwitchTo(dangerEvadeState.Bind(point));
    }

    private void CancelAll()
    {
        hasPending = false;
        RequestIdle(); // 立即停步；正在放技能时由行为层压到收招后再停
        SwitchTo(searchState);
    }

    private void StorePending(QuickMessageType type, TaskTarget target)
    {
        if (type == QuickMessageType.Attack && !CanAcceptAttackMessage()) return;
        if (type == QuickMessageType.ContestNeutral && FindNeutralStatue() == null) return;
        hasPending = true;
        pendingType = type;
        pendingTarget = target;
    }

    // 进攻在搜索/打珊瑚/追击时都响应；护送与进攻途中忽略（有目标时会自己打）
    private bool CanAcceptAttackMessage()
    {
        if (Current == escortState || Current == escortApproachState) return false;
        if (Current == assaultState || Current == assaultCombatState) return false;
        if (Current == dangerClearState) return false;
        return true;
    }

    protected DecisionState ConsumePendingOr(DecisionState fallback)
    {
        if (!hasPending) return fallback;
        hasPending = false;
        return BuildTaskState(pendingType, pendingTarget, fallback, true);
    }

    // 把任务消息翻译成目标状态；静态锚点在这里落定，动态锚点每帧读 sender 位置
    private DecisionState BuildTaskState(QuickMessageType type, TaskTarget target, DecisionState fallback, bool fromPending)
    {
        switch (type)
        {
            case QuickMessageType.RequestEscort:
                if (target.sender == null || !target.sender.Alive) return fallback;
                return escortApproachState.Bind(target.sender);
            case QuickMessageType.Rally:
                if (!target.hasPosition && (target.sender == null || !target.sender.Alive)) return fallback;
                return rallyApproachState.Bind(target);
            case QuickMessageType.Attack:
                if (!target.hasPosition && (target.sender == null || !target.sender.Alive)) return fallback;
                return assaultState.Bind(target);
            case QuickMessageType.ContestNeutral:
            {
                var neutral = FindNeutralStatue();
                if (neutral == null) return fallback; // 执行时再校验一次：中立单位已不存在
                return chaseState.Bind(neutral, searchState, GlobalConfig.ai_chase_limit);
            }
            default:
                return fallback;
        }
    }
    #endregion
}
