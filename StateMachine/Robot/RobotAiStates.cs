using Ros.Transport;
using UnityEngine;

// 人机玩家的各决策状态实现（状态图与消息矩阵见 Doc/08-游戏AI逻辑），只写期望行为、不碰执行层
public partial class RobotAi
{
    private sealed class SearchState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.Search;
        private readonly RobotAi robot;
        private bool hasPoint;
        private Vector3 point;
        private float resumeTime;
        private float nextGuardCheck;

        public SearchState(RobotAi robot) { this.robot = robot; }

        public override void Enter()
        {
            hasPoint = false;
            resumeTime = 0f;
        }

        public override DecisionState Decide()
        {
            // 近身敌人优先于守护点告警，避免被远处警报引走、自己在原地挨打
            var enemy = robot.NearestEnemy(false);
            if (enemy != null) return robot.chaseState.Bind(enemy, this, GlobalConfig.ai_chase_limit);

            if (robot.Owner.camp == EntityCamp.Defense && Time.time >= nextGuardCheck)
            {
                nextGuardCheck = Time.time + GlobalConfig.ai_guard_check_interval;
                var intruder = robot.FindGuardingEnemy();
                if (intruder != null) return robot.chaseState.Bind(intruder, this, GlobalConfig.ai_chase_limit);
            }

            // 珊瑚不还手，顺手打（打断规则最宽松的就是搜索战斗）
            var coral = robot.NearestEnemy(true);
            if (coral != null) return robot.searchCombatState.Bind(coral);

            if (hasPoint)
            {
                // 用执行层吸附后的目的地判距，避免「点被 NavMesh 吸附到脚下、状态机仍认为没到」而原地卡死
                if (robot.Owner.DistanceToNavDestination > GlobalConfig.ai_search_arrive) { robot.RequestMove(point); return null; }
                hasPoint = false;
                resumeTime = Time.time + UnityEngine.Random.Range(GlobalConfig.ai_search_pause_min, GlobalConfig.ai_search_pause_max);
                robot.RequestIdle();
                return null;
            }

            if (Time.time < resumeTime) return null; // 停顿中：保持 Idle

            Vector2 offset = UnityEngine.Random.insideUnitCircle * GlobalConfig.ai_search_radius;
            point = robot.Owner.transform.position + new Vector3(offset.x, 0f, offset.y);
            hasPoint = true;
            robot.RequestMove(point);
            return null;
        }

        public override DecisionState OnAllyChaseRequest(EntityData target) => robot.chaseState.Bind(target, this, GlobalConfig.ai_chase_limit);

        // 走不到就丢掉这个点，下一次决策重选
        public override void OnStuck()
        {
            hasPoint = false;
            resumeTime = 0f;
        }
    }

    private sealed class SearchCombatState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.SearchCombat;
        private readonly RobotAi robot;
        private EntityData coral;

        public SearchCombatState(RobotAi robot) { this.robot = robot; }

        public SearchCombatState Bind(EntityData target) { coral = target; return this; }

        public override DecisionState Decide()
        {
            // 唯一的"可立即打断"战斗态：真正的敌人一来就让位
            var enemy = robot.NearestEnemy(false);
            if (enemy != null) return robot.chaseState.Bind(enemy, robot.searchState, GlobalConfig.ai_chase_limit);

            if (coral == null || !coral.Alive) return robot.searchState;

            if (robot.FlatDistanceTo(coral) > robot.AttackRange) { robot.RequestMove(coral.transform.position); return null; }
            robot.RequestAttack(coral);
            return null;
        }

        public override DecisionState OnAllyChaseRequest(EntityData target) => robot.chaseState.Bind(target, robot.searchState, GlobalConfig.ai_chase_limit);
    }

    private sealed class ChaseState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.Chase;
        private readonly RobotAi robot;
        private EntityData target;
        private DecisionState returnState;
        private float limit;

        public ChaseState(RobotAi robot) { this.robot = robot; }

        private bool abandoned;

        // 目标走不到：放弃并暂时忽略它，回到来时的状态
        public override void OnStuck()
        {
            robot.IgnoreTarget(target);
            abandoned = true;
        }

        public ChaseState Bind(EntityData t, DecisionState ret, float limitRange)
        {
            target = t;
            returnState = ret;
            limit = limitRange;
            abandoned = false;
            return this;
        }

        public override DecisionState Decide()
        {
            if (abandoned) { abandoned = false; return returnState; }
            if (target == null || !target.Alive) return returnState;
            if (robot.FlatDistanceTo(target) > limit) return returnState;
            if (robot.FlatDistanceTo(target) <= robot.AttackRange) return robot.ResolveCombat(returnState).Bind(target, returnState);

            robot.RequestMove(target.transform.position);
            return null;
        }
    }

    // 战斗决策：四种战斗的差异全部由构造参数决定（撤离血线 / 活动范围 / 撤离去向）
    private sealed class CombatDecision : DecisionState
    {
        private readonly RobotAi robot;
        private float retreatRatio;
        private float activityRadius;
        private DecisionState retreatState;
        private EntityReportState reportState; // 同一个战斗类被 5 处复用，上报类型只能按实例区分

        public override EntityReportState ReportState => reportState;

        private EntityData target;
        private DecisionState returnState;
        private bool abandoned;

        // 追不上（目标在走不到的位置）：放弃并暂时忽略
        public override void OnStuck()
        {
            robot.IgnoreTarget(target);
            abandoned = true;
        }

        public CombatDecision(RobotAi robot) { this.robot = robot; }

        public void Setup(float retreatRatio, float activityRadius, DecisionState retreatState, EntityReportState reportState)
        {
            this.retreatRatio = retreatRatio;
            this.activityRadius = activityRadius;
            this.retreatState = retreatState;
            this.reportState = reportState;
        }

        public CombatDecision Bind(EntityData t, DecisionState ret)
        {
            target = t;
            returnState = ret;
            abandoned = false;
            return this;
        }

        public override bool BlocksTaskMessages => true;

        public override DecisionState Decide()
        {
            if (abandoned) { abandoned = false; return robot.ConsumePendingOr(returnState); }
            if (target == null || !target.Alive) return robot.ConsumePendingOr(returnState);

            if (robot.HealthRatio < retreatRatio) return retreatState;

            float dist = robot.FlatDistanceTo(target);
            if (activityRadius > 0f && dist > activityRadius) return robot.ConsumePendingOr(returnState);

            // 射程按「当前还有库存的技能」算：够得着才出招，够不着就前压，绝不在射程外空放
            if (dist > robot.AttackRange) { robot.RequestMove(target.transform.position); return null; }

            robot.RequestAttack(target);
            return null;
        }
    }

    private sealed class EscortApproachState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.EscortApproach;
        private readonly RobotAi robot;
        private EntityData escorted;
        private float enterTime;

        public EscortApproachState(RobotAi robot) { this.robot = robot; }

        public EscortApproachState Bind(EntityData target) { escorted = target; return this; }

        public override void Enter() { enterTime = Time.time; }

        public override DecisionState Decide()
        {
            if (escorted == null || !escorted.Alive) return robot.searchState;
            if (Time.time - enterTime > GlobalConfig.ai_escort_timeout) return robot.searchState;

            if (robot.FlatDistanceTo(escorted) <= GlobalConfig.ai_escort_arrive) return robot.escortState.Bind(escorted);

            robot.RequestMove(escorted.transform.position);
            return null;
        }
    }

    private sealed class EscortState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.Escort;
        private readonly RobotAi robot;
        private EntityData escorted;
        private Vector3 slotOffset;

        public EscortState(RobotAi robot) { this.robot = robot; }

        public EscortState Bind(EntityData target) { escorted = target; return this; }

        // 进入时随机定一个相对站位，之后始终去"目标位置 + 偏移"，多个人机不会挤在一起
        public override void Enter()
        {
            Vector2 offset = UnityEngine.Random.insideUnitCircle * GlobalConfig.ai_escort_slot_radius;
            slotOffset = new Vector3(offset.x, 0f, offset.y);
        }

        public override DecisionState Decide()
        {
            if (escorted == null || !escorted.Alive) return robot.searchState;

            var enemy = robot.NearestEnemyAround(escorted.transform.position, GlobalConfig.ai_escort_combat_radius, false);
            if (enemy != null) return robot.escortCombatState.Bind(enemy, this);

            Vector3 slot = escorted.transform.position + slotOffset;
            float dist = robot.FlatDistance(slot);
            // 追上与停下用不同阈值：锚点随护送对象移动，单阈值会在边界反复走停
            if (dist > GlobalConfig.ai_escort_follow) { robot.RequestMove(slot); return null; }
            if (dist <= GlobalConfig.ai_escort_follow * GlobalConfig.ai_hysteresis_ratio) robot.RequestIdle(escorted);

            return null; // 滞回带：保持当前行为
        }
    }

    private sealed class DangerEvadeState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.DangerEvade;
        private readonly RobotAi robot;
        private Vector3 threat;
        private float enterTime;
        private Vector3 escapeTo;

        public DangerEvadeState(RobotAi robot) { this.robot = robot; }

        public DangerEvadeState Bind(Vector3 point) { threat = point; return this; }

        public override bool BlocksTaskMessages => true;

        public override void Enter()
        {
            enterTime = Time.time;
            Vector3 away = robot.Owner.transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = -robot.Owner.transform.forward;
            escapeTo = robot.Owner.transform.position + away.normalized * GlobalConfig.ai_evade_distance;
        }

        public override DecisionState Decide()
        {
            if (Time.time - enterTime >= GlobalConfig.ai_evade_duration) return robot.ConsumePendingOr(robot.searchState);
            if (robot.FlatDistance(threat) >= GlobalConfig.ai_evade_distance) return robot.ConsumePendingOr(robot.searchState);

            robot.RequestMove(escapeTo);
            return null;
        }
    }

    private sealed class DangerClearState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.DangerClear;
        private readonly RobotAi robot;
        private Vector3 point;
        private float enterTime;

        public DangerClearState(RobotAi robot) { this.robot = robot; }

        public DangerClearState Bind(Vector3 value) { point = value; return this; }

        public override void Enter() { enterTime = Time.time; }

        public override DecisionState Decide()
        {
            if (Time.time - enterTime > GlobalConfig.ai_danger_clear_timeout) return robot.searchState;

            // 只对玩家/人机让路
            var enemy = robot.NearestHighThreatAround(point, GlobalConfig.ai_danger_clear_radius);
            if (enemy != null) return robot.chaseState.Bind(enemy, this, GlobalConfig.ai_chase_limit);

            if (robot.FlatDistance(point) > GlobalConfig.ai_danger_clear_arrive) robot.RequestMove(point);
            else robot.RequestIdle();
            return null;
        }
    }

    private sealed class RallyApproachState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.RallyApproach;
        private readonly RobotAi robot;
        private TaskTarget anchor;

        public RallyApproachState(RobotAi robot) { this.robot = robot; }

        public RallyApproachState Bind(TaskTarget target) { anchor = target; return this; }

        public override DecisionState Decide()
        {
            if (!robot.TryResolveAnchor(anchor, out var point)) return robot.searchState;

            if (robot.FlatDistance(point) <= GlobalConfig.ai_rally_arrive) return robot.rallyWaitState.Bind(anchor);

            robot.RequestMove(point);
            return null;
        }
    }

    private sealed class RallyWaitState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.RallyWait;
        private readonly RobotAi robot;
        private TaskTarget anchor;
        private float nextCheck;

        public RallyWaitState(RobotAi robot) { this.robot = robot; }

        public RallyWaitState Bind(TaskTarget target) { anchor = target; return this; }

        public override DecisionState Decide()
        {
            if (!robot.TryResolveAnchor(anchor, out var point)) return robot.searchState;

            if (Time.time >= nextCheck)
            {
                nextCheck = Time.time + GlobalConfig.ai_guard_check_interval;
                var enemy = robot.NearestEnemyAround(point, GlobalConfig.ai_rally_keep, false);
                if (enemy != null)
                {
                    robot.GroupCall(enemy, GlobalConfig.ai_rally_call_radius); // 叫上附近人机一起守
                    return robot.rallyGuardState.Bind(enemy, this);
                }
            }

            float dist = robot.FlatDistance(point);
            // 靠近与停下用不同阈值：锚点可能跟着发送者移动，单阈值会在边界反复走停
            if (dist > GlobalConfig.ai_rally_keep) { robot.RequestMove(point); return null; }
            if (dist <= GlobalConfig.ai_rally_keep * GlobalConfig.ai_hysteresis_ratio) robot.RequestIdle();

            return null; // 滞回带：保持当前行为
        }
    }

    private sealed class AssaultState : DecisionState
    {
        public override EntityReportState ReportState => EntityReportState.Assault;
        private readonly RobotAi robot;
        private TaskTarget anchor;
        private bool abandoned;

        public AssaultState(RobotAi robot) { this.robot = robot; }

        public AssaultState Bind(TaskTarget target) { anchor = target; abandoned = false; return this; }

        // 锚点走不到（建筑底座挡路等）：放弃进攻点回搜索自己找仗打，否则会一直对着同一个点原地站着
        public override void OnStuck() { abandoned = true; }

        public override DecisionState Decide()
        {
            if (abandoned) { abandoned = false; return robot.searchState; }
            if (!robot.TryResolveAnchor(anchor, out var point)) return robot.searchState;

            // 路上被「珊瑚/鲛人以外的敌人」贴进攻击范围：直接开打，打完回协同进攻继续赶路
            var threat = robot.NearestAssaultThreat(robot.AttackRange);
            if (threat != null) return robot.assaultCombatState.Bind(threat, this);

            if (!anchor.hasPosition) { robot.RequestMove(point); return null; } // 动态锚点：一路跟着发送者

            // 执行层已停在导航终点也算到达：锚点吸附/算不出路时不会再一直重发移动请求而原地站着
            if (robot.FlatDistance(point) > GlobalConfig.ai_assault_arrive && !robot.Owner.NavArrived)
            {
                robot.RequestMove(point);
                return null;
            }
            return robot.searchState; // 静态锚点到达且无敌：继续推
        }
    }
}
