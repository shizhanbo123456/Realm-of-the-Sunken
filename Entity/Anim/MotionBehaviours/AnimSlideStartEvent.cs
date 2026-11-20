using UnityEngine;

using Ros.Config;

public class AnimSlideStartEvent : AnimMotionEvent
{
    protected override InputBlockOp blockOp => InputBlockOp.Jump | InputBlockOp.Slide | InputBlockOp.Attack;
    public override EndureType? Endure => EndureType.Super;
    [SerializeField] private AnimationCurve speedCurve;
    private float slideTime;
    private bool canEnd;
    public override void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateEnter(animator, stateInfo, layerIndex);
        if (data == null) return;
        if (main)
        {
            slideTime = 0;
            canEnd = true;
            data.effectController.AddEffect(EffectType.DodgeBoost,
                GlobalVariable.I(global_variablesEnum.buff_dodge_level),
                GlobalVariable.F(global_variablesEnum.buff_dodge_duration));
        }
    }
    public override void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        base.OnStateUpdate(animator, stateInfo, layerIndex);
        if (main)
        {
            anim.SetVelocityForward(speedCurve.Evaluate(stateInfo.normalizedTime) * MoveSpeed);
            anim.SetVelocityVertical(GroundAdsorptionSpeed);
            slideTime += Time.deltaTime;
            if (slideTime > 0.2f && canEnd)
            {
                anim.EndSlide();
                canEnd = false;
            }
        }
    }
}
