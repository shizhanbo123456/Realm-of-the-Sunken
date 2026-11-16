using UnityEngine;

// 鲛人：无目标时在进入游荡的位置附近游荡；普通鲛人不限距离索敌、连续追击打不到就回收，精英鲛人索敌随游荡时长变远
public class ZombieStateMachine : DecisionMachine, IBehaviorHost
{
    private const int SlotClawRight = 0;
    private const int SlotClawLeft = 1;
    private const int SlotRoar = 2;

    public readonly ZombieEntityData Owner;

    private readonly BehaviorArbiter arbiter;
    private readonly WanderState wanderState;
    private readonly ChaseState chaseState;
    private readonly AttackState attackState;

    private Vector3 wanderCenter;
    private Vector3 wanderPoint;
    private bool hasWanderPoint;
    private float wanderResumeTime;
    private float noTargetSince;

    // 普通鲛人不按距离筛选索敌；精英鲛人仍按视野基准 + 未锁定时间递增
    private readonly bool noDistanceFilter;
    private float chaseElapsed;  // 追击态累计时长（进入攻击态清零，中途回游荡不清零）
    private float chaseTickTime; // 上一次累计时刻

    private ushort targetId;
    private EntityData currentTarget;
    private bool clawLeftNext;

    public ZombieStateMachine(ZombieEntityData owner)
    {
        Owner = owner;
        noTargetSince = Time.time;
        noDistanceFilter = owner.type.category == EntityCategory.Zombie;
        ThinkInterval = GlobalConfig.zombie_decide_interval;

        wanderState = new WanderState(this);
        chaseState = new ChaseState(this);
        attackState = new AttackState(this);
        arbiter = new BehaviorArbiter(this);
        Initialize(wanderState, owner.AutoStaggerPhase);
    }

    protected override EntityData MachineOwner => Owner;

    public override void Tick()
    {
        if (!Owner.Alive) return;
        base.Tick();
        arbiter.Tick(Time.deltaTime);
    }

    #region//Local
    private ushort ignoredTargetId;
    private float ignoreTargetUntil;

    // 每 tick 先刷新目标，三个状态共用同一份结论
    protected override void OnThink()
    {
        // 目标连续算不出路：忽略一段时间，避免放弃后下一拍又锁回同一个走不到的目标
        if (Owner.NavUnreachable && currentTarget != null)
        {
            ignoredTargetId = currentTarget.id;
            ignoreTargetUntil = Time.time + GlobalConfig.ai_ignore_unreachable_time;
            ClearTarget();
        }

        currentTarget = AcquireTarget();
        // 锁到目标就把计时清零：丢掉目标后从 1 倍重新累积
        if (currentTarget != null) noTargetSince = Time.time;
    }

    private bool IsIgnored(EntityData entity)
        => entity != null && entity.id == ignoredTargetId && Time.time < ignoreTargetUntil;

    private EntityData AcquireTarget()
    {
        float range = noDistanceFilter ? GlobalConfig.zombie_acquire_unlimited : AcquireRange;
        // 已锁定的目标只要还活着就一直追，不再按距离重新判定
        if (targetId != 0 && BattleManager.EntityContainer.Entities.TryGetObject(targetId, out var locked))
        {
            if (locked != null && locked.Alive && !IsIgnored(locked)) return locked;
        }

        var nearest = BattleManager.EntityContainer.GetNearestEnemy(Owner, range);
        if (IsIgnored(nearest)) nearest = null;
        targetId = nearest != null ? nearest.id : (ushort)0;
        return nearest;
    }

    // 精英鲛人 1 倍索敌 = 视野夹到「追击基准 - 余量」；未锁定时按秒线性放大，游荡 10s 两倍、20s 三倍，不设上限
    private float AcquireRange
    {
        get
        {
            float view = Owner.attributes.ViewDistance > 0f
                ? Owner.attributes.ViewDistance
                : GlobalConfig.zombie_acquire_range;
            float baseRange = Mathf.Min(view, GlobalConfig.zombie_max_chase_distance - GlobalConfig.zombie_acquire_margin);
            return baseRange * (1f + (Time.time - noTargetSince) * GlobalConfig.zombie_acquire_scale_grow);
        }
    }

    private float FlatDistance(Vector3 point)
    {
        Vector3 d = point - Owner.transform.position;
        d.y = 0f;
        return d.magnitude;
    }

    private void ClearTarget()
    {
        targetId = 0;
        currentTarget = null;
    }

    // 追击累计只在追击态推进；追到上限说明一直追不上也打不到，判定卡死并回收
    private bool ChaseTimedOut()
    {
        chaseElapsed += Time.time - chaseTickTime;
        chaseTickTime = Time.time;
        if (chaseElapsed < GlobalConfig.zombie_chase_timeout) return false;
        Owner.Discard();
        return true;
    }

    private bool CanUseSlot(int slot) => IsSkillReady(SkillIdAt(slot));

    // 左右爪交替：主手不放再试另一只，避免同一只手连击；普通攻击不计冷却
    private bool TryClaw()
    {
        clawLeftNext = !clawLeftNext;
        int first = clawLeftNext ? SlotClawLeft : SlotClawRight;
        if (TryUseSkill(SkillIdAt(first))) return true;
        return TryUseSkill(SkillIdAt(clawLeftNext ? SlotClawRight : SlotClawLeft));
    }

    private void RequestIdle() => arbiter.RequestIdle();

    private void RequestMove(Vector3 point) => arbiter.RequestMove(point);

    private void RequestChase(EntityData target) => arbiter.RequestChase(target);

    private void RequestAttack(EntityData target) => arbiter.RequestAttack(target);
    #endregion

    #region//IBehaviorHost
    EntityData IBehaviorHost.Owner => Owner;

    public void Stop() => Owner.Stop();

    public void FaceTarget(EntityData target) => Owner.SetFaceTarget(target != null ? target.id : (ushort)0);

    // 远距离用嘶吼（计冷却），近距离用爪击（不计冷却）
    public bool TryAttack(EntityData target)
    {
        if (target == null) return false;
        if (FlatDistance(target.transform.position) > GlobalConfig.zombie_roar_range) return TryUseSkill(SkillIdAt(SlotRoar));
        return TryClaw();
    }

    public bool IsActing => Owner.anim != null && Owner.anim.CurrentState == EntityAnim.AnimState.Attack;

    public void ClearFace() => Owner.SetFaceTarget(0);

    // 卡住由 OnThink 统一处理（忽略该目标一段时间），行为层不必额外做
    public void NotifyStuck() { }
    #endregion

    #region//DecisionStates
    private sealed class WanderState : DecisionState
    {
        private readonly ZombieStateMachine machine;

        public WanderState(ZombieStateMachine machine) { this.machine = machine; }

        // 进入游荡时以当前位置为新圆心，丢掉上一轮的目标点：跑远后不会掉头回出生点
        public override void Enter()
        {
            machine.Owner.SetFaceTarget(0); // 已无目标，别再朝旧目标转
            machine.wanderCenter = machine.Owner.transform.position;
            machine.hasWanderPoint = false;
            machine.wanderResumeTime = 0f;
        }

        public override DecisionState Decide()
        {
            if (machine.currentTarget != null) return machine.chaseState;

            if (machine.hasWanderPoint)
            {
                if (machine.FlatDistance(machine.wanderPoint) > ZombieEntityData.ArriveRadius)
                {
                    machine.RequestMove(machine.wanderPoint); // 还在路上
                    return null;
                }
                machine.hasWanderPoint = false;
                machine.wanderResumeTime = Time.time + GlobalConfig.zombie_wander_pause;
                machine.RequestIdle();
                return null;
            }

            if (Time.time < machine.wanderResumeTime)
            {
                machine.RequestIdle(); // 到点后的停顿
                return null;
            }

            Vector2 offset = Random.insideUnitCircle * GlobalConfig.zombie_wander_radius;
            machine.wanderPoint = machine.wanderCenter + new Vector3(offset.x, 0f, offset.y);
            machine.hasWanderPoint = true;
            machine.RequestMove(machine.wanderPoint);
            return null;
        }
    }

    private sealed class ChaseState : DecisionState
    {
        private readonly ZombieStateMachine machine;

        public ChaseState(ZombieStateMachine machine) { this.machine = machine; }

        public override void Enter()
        {
            machine.Owner.SetFaceTarget(0); // 追击时朝向跟着行进方向
            machine.chaseTickTime = Time.time;
        }

        public override DecisionState Decide()
        {
            // 普通鲛人：连续追击累计到上限仍未进入攻击态 → 直接回收
            if (machine.noDistanceFilter && machine.ChaseTimedOut()) return null;

            var target = machine.currentTarget;
            if (target == null) return machine.wanderState;

            // 目标活着就一直追：不按距离放弃，避免刚锁到就被距离判回游荡
            float dist = machine.FlatDistance(target.transform.position);
            if (dist <= GlobalConfig.zombie_attack_range) return machine.attackState;

            // 远处且嘶吼冷却好了：进攻击态放一嗓子，放完由攻击态切回追击
            if (dist > GlobalConfig.zombie_roar_range && machine.CanUseSlot(SlotRoar)) return machine.attackState;

            machine.RequestChase(target); // 跟随目标物体
            return null;
        }
    }

    private sealed class AttackState : DecisionState
    {
        private readonly ZombieStateMachine machine;

        public AttackState(ZombieStateMachine machine) { this.machine = machine; }

        // 进攻击态即清零追击累计：卡死计时只统计"一直在追、始终打不到"的时段
        public override void Enter() => machine.chaseElapsed = 0f;

        public override DecisionState Decide()
        {
            var target = machine.currentTarget;
            if (target == null) return machine.wanderState; // 目标丢了：直接回游荡，不必经追击态多等一拍

            // 动作没播完：压住，不出新招也不切状态
            if (machine.IsActing)
            {
                machine.RequestAttack(target);
                return null;
            }

            float dist = machine.FlatDistance(target.transform.position);

            if (dist <= GlobalConfig.zombie_attack_range)
            {
                machine.RequestAttack(target);
                return null;
            }

            if (dist > GlobalConfig.zombie_roar_range && machine.CanUseSlot(SlotRoar))
            {
                machine.RequestAttack(target);
                return null;
            }

            // 退出距离大于进入距离：目标在攻击距离边界晃动时不会在追击/攻击之间反复切换
            if (dist > GlobalConfig.zombie_attack_range * GlobalConfig.zombie_attack_exit_scale) return machine.chaseState;

            // 退出缓冲区内继续贴近，保持攻击态，不切状态也不出招
            machine.RequestChase(target);
            return null;
        }
    }
    #endregion
}
