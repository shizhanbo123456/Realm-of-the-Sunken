using UnityEngine;

public class AnimEvent : StateMachineBehaviour
{
    protected virtual EntityAnim.AnimState State { get; }
    protected virtual InputBlockOp blockOp { get; } = InputBlockOp.None;
    protected virtual bool UseGravity { get; } = true;
    public virtual EndureType? Endure { get; }
    public virtual EntityAnim.WeaponPos ShowWeapon { get; } = EntityAnim.WeaponPos.Show;
    public virtual bool AllowCombo { get; } = false;
    [Tooltip("处于该片段时每秒蓄力增量；负值 = 每秒倒扣，默认 -1")]
    [SerializeField] private float chargeRate = -1f;
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
        AnimId = stateInfo.fullPathHash;
        anim.NotifyStateEnter(AnimId, State); // 客户端同样要推进当前状态：播放倍率按阶段合成，两端同一套公式
        if (data == null) return;

        data.SetGravityEnabled(UseGravity);
        data.SetInputBlock(blockOp);
        data.SetEndureOverride(Endure);
    }

    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateUpdate(animator, stateInfo, layerIndex);
        if (!main || data == null) return; // 客户端表现视图没有 EntityData，不参与蓄力
        anim.AddCharge(chargeRate * Time.deltaTime);
    }
}
