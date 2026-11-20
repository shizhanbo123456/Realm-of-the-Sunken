using Ros.Config;
using UnityEngine;

public class AnimAttackEvent : AnimEvent
{
    protected override EntityAnim.AnimState State => EntityAnim.AnimState.Attack;
    protected override InputBlockOp blockOp =>type== EntityAnim.AttackType.Combo? 
        InputBlockOp.Rotation | InputBlockOp.Attack:
        InputBlockOp.Forward|InputBlockOp.Rotation|InputBlockOp.Jump|InputBlockOp.Slide|InputBlockOp.Attack;
    protected override bool UseGravity => AttackClipUseGravity;
    public override EndureType? Endure => EndureType.Super;
    public override EntityAnim.WeaponPos ShowWeapon => WeaponInHand ? EntityAnim.WeaponPos.Hand : EntityAnim.WeaponPos.Show;
    public override bool AllowCombo => AllowComboAttack;
    [SerializeField] private EntityAnim.AttackType type;
    [SerializeField] private bool AttackClipUseGravity;
    [SerializeField] private bool WeaponInHand=true;
    [SerializeField]private bool AllowComboAttack = false;
    [Tooltip("攻击吸附：起手把 2~2.5m 内的最近敌人拉近到 1.5m，只对玩家角色生效")]
    [SerializeField] private bool AttackAdhesion;
    [SerializeField] private AnimationCurve speedForward;//是位置，导数才是速度
    [SerializeField] private AnimationCurve speedUpward;//是位置，导数才是速度
    [SerializeField][Range(0,1)] private float threshold;
    private bool canTrigAttack;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateEnter(animator,stateInfo,layerIndex);
        canTrigAttack = true;   // 客户端无 EntityData：仍允许攻击帧触发 onAttack（特效/表现用），不依赖 data
        if (data == null) return;
        if (!main) return;
        data.hitbackVelocity = Vector3.zero; // 起手清除击退，避免攻击时带惯性漂移
        if (AttackAdhesion) AdhereToNearestEnemy();
    }

    // 吸附索敌同技能口径（敌对阵营、排除自己、珊瑚不参与），只处理 2~2.5m 这段够不到的缝隙
    private void AdhereToNearestEnemy()
    {
        if (data.type.category != EntityCategory.Character_Attack
            && data.type.category != EntityCategory.Character_Defense) return;
        float maxDist = GlobalVariable.F(global_variablesEnum.attack_adhesion_search_distance);
        float minDist = GlobalVariable.F(global_variablesEnum.attack_adhesion_min_distance);
        EntityCamp hostile = EntityCampUtil.HostileOf(data.type.category);
        EntityData target = BattleManager.EntityContainer.GetNearestInCamp(
            data.transform.position, maxDist, hostile, data.id, true);
        if (target == null) return;
        Vector3 to = target.transform.position - data.transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        if (dist < minDist || dist > maxDist) return;
        // 终点 = 敌人位置沿连线退 stopDist：匀减速走完这段，到点正好停在贴身距离
        Vector3 dest = target.transform.position - to / dist
            * GlobalVariable.F(global_variablesEnum.attack_adhesion_stop_distance);
        data.FaceTowards(target.transform.position);
        data.SetMotion(new MotionDecelerate(dest,
            GlobalVariable.F(global_variablesEnum.attack_adhesion_speed_start),
            GlobalVariable.F(global_variablesEnum.attack_adhesion_speed_end)));
    }
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateUpdate(animator, stateInfo, layerIndex);
        if (!main) return;

        float nt=stateInfo.normalizedTime;
        float forward = (speedForward.Evaluate(nt+0.01f)- speedForward.Evaluate(nt - 0.01f))*50/stateInfo.length;
        float upward = (speedUpward.Evaluate(nt+0.01f)- speedUpward.Evaluate(nt - 0.01f))*50 / stateInfo.length;
        anim.SetVelocityForward(forward*anim.animData.legHeight);
        anim.SetVelocityVertical(upward);

        // 过攻击帧后每帧重试：回调还没注册时保留待触发，不白丢这一次
        if (!canTrigAttack || nt <= threshold) return;
        var callback = anim.onAttack;
        if (callback == null) return;
        canTrigAttack = false;
        callback.Invoke(type);
    }
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateExit(animator, stateInfo, layerIndex);
    }
}
