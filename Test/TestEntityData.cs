using UnityEngine;
using Ros.Config;

public class TestEntityData : EntityData
{
    private float turnInput; // -1 左 / +1 右

    [Tooltip("动作集类型")]
    [SerializeField] private EntityAnim.CharcterAnimType testAnimType;
    [Tooltip("武器编号（-1 = 无武器）")]
    [SerializeField] private int testWeaponId = -1;
    [Tooltip("武器显示类型")]
    [SerializeField] private EntityAnim.WeaponDisplayType testWeaponDisplay;

    public bool TestMoving => moveInput.sqrMagnitude > 0.0001f;

    public void SetTestInput(bool forward, float turn)
    {
        turnInput = turn;
        SetMoveInput(forward ? Vector3.forward : Vector3.zero);
    }

    public void SetupTest()
    {
        anim = GetComponentInChildren<EntityAnim>();

        // SetType 必须在 Init 之后：EntityAnim 的 animators 列表在 Init 里才收集
        if (anim != null) anim.Init(this);
        if (anim != null)
        {
            anim.SetType(testAnimType);
            anim.SetWeapon(testWeaponId, testWeaponDisplay);
            anim.DoSpawn();
        }

        // 测试场景常无相机对准角色：默认剔除模式会停更状态机、断掉速度声明
        foreach (var animator in GetComponentsInChildren<Animator>(true))
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        SetupTestBody();
        GetComponentInChildren<EntityModelInfo>().BuildCapsuleCollider();
    }

    public override void OnTickMove(float deltaTime, bool canInput)
    {
        float yaw = transform.eulerAngles.y;
        if (canInput && MotionCanMove && Mathf.Abs(turnInput) > 0.01f)
            yaw += GlobalVariable.F(global_variablesEnum.move_turn_rate) * Mathf.Sign(turnInput) * deltaTime;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    #region//Local
    private void SetupTestBody()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = true;
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.interpolation = RigidbodyInterpolation.None;
        rb.drag = GlobalConfig.rb_drag;
        rb.angularDrag = GlobalConfig.rb_angular_drag;
    }

    public string DescribeState()
    {
        Vector3 v = rb != null ? rb.velocity : Vector3.zero;
        return $"state={(anim != null ? anim.CurrentState.ToString() : "无anim")} grounded={grounded} " +
               $"moveInput={moveInput} v={v:F2}";
    }
    #endregion
}
