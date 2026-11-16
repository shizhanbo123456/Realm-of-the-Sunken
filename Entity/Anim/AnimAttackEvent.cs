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

        if (canTrigAttack && stateInfo.normalizedTime > threshold)
        {
            canTrigAttack = false;
            OnAttack();
        }
    }
    public override void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateExit(animator, stateInfo, layerIndex);
    }
    private void OnAttack()
    {
        if (anim.onAttack != null) anim.onAttack.Invoke(type);
    }
}
