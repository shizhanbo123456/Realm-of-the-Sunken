using UnityEngine;

public class AnimEvent : StateMachineBehaviour
{
    protected virtual EntityAnim.AnimState State { get; }
    protected virtual InputBlockOp blockOp { get; } = InputBlockOp.None;
    protected virtual bool UseGravity { get; } = true;
    public virtual EndureType? Endure { get; }
    public virtual EntityAnim.WeaponPos ShowWeapon { get; } = EntityAnim.WeaponPos.Show;
    public virtual bool AllowCombo { get; } = false;
    //用于传递给客户端识别动画片段
    public int AnimId { get; private set; } = -1;

    protected EntityAnim anim;
    protected EntityData data;
    protected bool main;
    protected bool MovingForward => data.IsMovingForward;
    protected float MoveSpeed=>anim.animData.RunSpeed;
    protected float JumpSpeed=>anim.animData.JumpSpeed;
    protected bool initialized = false;

    protected float GroundAdsorptionSpeed = -0.5f;

    public void Init(EntityAnim anim, EntityData data,bool mainAnim)//仅仅主动画机的片段会触发事件
    {
        this.anim = anim;
        this.data = data;
        main = mainAnim;
        initialized = true;
    }

    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateEnter(animator, stateInfo, layerIndex);
        if (!main) return;
        anim.SetWeaponPos(ShowWeapon); // 武器显示只跟动画状态走：客户端与首页角色（无 EntityData）同样要切
        anim.SetAllowCombo(AllowCombo); // 连招窗口同样只跟动画状态走
        if (data == null) return;
        AnimId = stateInfo.fullPathHash;
        anim.NotifyStateEnter(AnimId, State);

        data.SetGravityEnabled(UseGravity);
        data.SetInputBlock(blockOp);
        data.SetEndureOverride(Endure);
    }
}
