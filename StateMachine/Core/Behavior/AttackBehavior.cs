using UnityEngine;

// 通用攻击：停步 + 朝向 + 出手；动作播完前不允许切走，播放期间按节拍自动续招
public sealed class AttackBehavior : Behavior
{
    private readonly IBehaviorHost host;

    private EntityData target;
    private float nextTryTime;

    public AttackBehavior(IBehaviorHost host) { this.host = host; }

    public override BehaviorKind Kind => BehaviorKind.Attack;

    public override bool CanInterrupt => !host.IsActing;

    public override void Bind(in BehaviorRequest request)
    {
        target = request.Target;
        host.Stop();
        host.FaceTarget(target);
        host.TryAttack(target);
        nextTryTime = Time.time + GlobalConfig.attack_retry_interval; // 起手这一拍已经试过，别在同一帧重复出招
    }

    // 动作播放中反复试招：普攻由宿主在连招窗口内续段，技能没冷却好/够不着就自然放不出来
    public override void Tick(float deltaTime)
    {
        if (!host.IsActing) return;
        if (Time.time < nextTryTime) return;
        nextTryTime = Time.time + GlobalConfig.attack_retry_interval;
        host.TryAttack(target);
    }

    public override void Exit() => host.ClearFace();
}
