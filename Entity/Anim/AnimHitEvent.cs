using UnityEngine;

public class AnimHitEvent : AnimEvent
{
    protected override EntityAnim.AnimState State => EntityAnim.AnimState.Hit;
    protected override InputBlockOp blockOp => InputBlockOp.Forward | InputBlockOp.Rotation | InputBlockOp.Jump | InputBlockOp.Slide | InputBlockOp.Attack;
    [SerializeField] private int protection = 10;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateEnter(animator, stateInfo, layerIndex);
        if (data == null) return;
        if (main)
        {
            anim.hitProtection += protection;
            if (anim.hitProtection >= 100)
            {
                anim.hitProtection = 0;
                anim.onHitProtction?.Invoke();
            }
        }
    }
}
