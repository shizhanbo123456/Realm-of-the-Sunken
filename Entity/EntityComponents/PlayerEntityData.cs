using Ros.Transport;
using UnityEngine;
using Ros.Config;

public class PlayerEntityData : EntityData
{
    private class MoveState
    {
        public float yaw;       // 角色朝向（度），由 CSMove 的 yaw 直接写入
        public float yawSpeed;  // 绕 Y 角速度（度/秒，客户端推演用）；瞬时转向恒为 0
    }

    private MoveState moveState;

    private bool flightMode;
    private bool flyUpHeld;   // 飞行上升长按（跳跃键）
    private bool flyDownHeld; // 飞行下降长按（滑铲键）

    public override bool FlightMode => flightMode;

    // GM 飞行开关：仅真人玩家使用（与 AI 控制互斥，由数据保证）
    public void SetFlightMode(bool enabled)
    {
        if (flightMode == enabled) return;
        flightMode = enabled;
        if (enabled)
        {
            flyUpHeld = false;
            flyDownHeld = false;
            SetGravityEnabled(false); // 关重力（UpdateGrounded 随之停用，每帧再由 TickVelocity 强制）
            grounded = true;          // 飞行视作贴地：全程 InAir=false，动画走地面状态
            if (anim != null) anim.InAir(false);
        }
        else
        {
            SetGravityEnabled(true); // 恢复重力，落地/悬空交回地面检测
        }
    }

    public override float YawSpeed => moveState != null ? moveState.yawSpeed : 0f;

    // 移动：瞬时转向 yaw（客户端按相机 yaw 合成好的世界朝向）后沿自身前方前进
    // 朝向被屏蔽时保持当前朝向继续前进（攻击动画中可以走、不转身）
    public void MoveTo(float yaw)
    {
        EnsureMoveState();
        if (InputBlocked(InputBlockOp.Forward)) return; // 前后被屏蔽：不接受新的移动
        if (!InputBlocked(InputBlockOp.Rotation)) moveState.yaw = Mathf.Repeat(yaw, 360f);
        SetMoveInput(Vector3.forward);
        if (anim != null) anim.Move(true);
    }

    public void StopMove()
    {
        EnsureMoveState();
        SetMoveInput(Vector3.zero);
        if (anim != null) anim.Move(false);
    }

    // 跳跃键：按下起跳，飞行模式下转为上升长按；抬起只停上升，不影响已起跳的动作
    public void SetJumpHeld(bool down)
    {
        if (!down)
        {
            flyUpHeld = false;
            return;
        }
        if (InputBlocked(InputBlockOp.Jump)) return;
        flyUpHeld = true;
        if (!flightMode)
        {
            Jump();
        }
    }

    // 滑铲键：按下切进滑铲（时长由动画模块决定），飞行模式下转为下降长按
    public void SetSlideHeld(bool down)
    {
        if (!down)
        {
            flyDownHeld = false;
            return;
        }
        if (InputBlocked(InputBlockOp.Slide)) return;
        flyDownHeld = true;
        if (!flightMode && anim != null) anim.DoSlide();
    }

    // 设置屏蔽的瞬间立即收手：前后被屏蔽 = 停下，动作键被屏蔽 = 松手（不等下一次输入）
    public override void SetInputBlock(InputBlockOp op)
    {
        base.SetInputBlock(op);
        SetInputReleased(op);
    }

    public override void SetInputReleased(InputBlockOp op)
    {
        if ((op & InputBlockOp.Forward) != 0) StopMove();
        if ((op & InputBlockOp.Jump) != 0) flyUpHeld = false;
        if ((op & InputBlockOp.Slide) != 0) flyDownHeld = false;
    }

    private void Jump()
    {
        if (anim != null) anim.DoJump();
    }

    public override void OnTickMove(float deltaTime, bool canInput)
    {
        EnsureMoveState();
        if (moveState == null) return;

        // 飞行：垂直速度声明（长按跳跃键升 / 长按滑铲键降，固定速度）；水平移动与动画仍走原有地面链路
        if (FlightMode)
        {
            float v = 0f;
            if (flyUpHeld) v = GlobalVariable.F(global_variablesEnum.fly_vertical_speed);
            else if (flyDownHeld) v = -GlobalVariable.F(global_variablesEnum.fly_vertical_speed);
            SetVelocityVertical(v, VelocitySource.Flight);
        }

        if (robotControlled || aiControlled)
        {
            TickRobotMove(deltaTime, canInput);
            transform.rotation = Quaternion.Euler(0f, moveState.yaw, 0f);
            return;
        }

        // 真人的朝向只由 CSMove 决定：这里不逐帧转身，只把逻辑朝向写回 transform
        moveState.yawSpeed = 0f;
        transform.rotation = Quaternion.Euler(0f, moveState.yaw, 0f);
    }

    // 自动索敌转向：必须同时写逻辑朝向，否则下一帧 OnTickMove 会用旧 yaw 把 transform 盖回去
    public override void FaceTowards(Vector3 worldPoint)
    {
        EnsureMoveState();
        Vector3 to = worldPoint - transform.position;
        to.y = 0f;
        if (to.sqrMagnitude < 0.0001f) return; // 目标与自己重合，方向无意义
        moveState.yaw = Quaternion.LookRotation(to).eulerAngles.y;
        moveState.yawSpeed = 0f;
        transform.rotation = Quaternion.Euler(0f, moveState.yaw, 0f);
    }

    #region Robot 驱动入口（人机；真人玩家不走这里）
    private RobotAi robot;

    // AI 操控（外部指令驱动）：与人机互斥的另一种自动驱动，复用同一套 Nav 移动分支
    public bool aiControlled;
    public AiIntentRelay AiRelay;

    public override void TickAuto()
    {
        if (robotControlled)
        {
            EnsureRobot();
            robot.Tick();
            return;
        }
        if (!aiControlled || AiRelay == null) return;
        AiRelay.Tick();
    }

    public void OnQuickMessage(SCQuickMessage message)
    {
        if (!robotControlled) return;
        EnsureRobot();
        robot.OnQuickMessage(message);
    }

    // 队友人机请求一起追击某个敌人（群体呼叫）
    public void RequestRobotChase(EntityData target)
    {
        if (!robotControlled) return;
        EnsureRobot();
        robot.RequestChase(target);
    }

    private void EnsureRobot()
    {
        if (robot == null) robot = new RobotAi(this);
    }

    private Vector3 robotFacePoint;
    private bool robotFaceSet;

    public void SetRobotFacePoint(Vector3 worldPoint)
    {
        robotFacePoint = worldPoint;
        robotFaceSet = true;
    }

    public void ClearRobotFacePoint() => robotFaceSet = false;

    private void TickRobotMove(float deltaTime, bool canInput)
    {
        // 强控/位移锁输入期间不推进：清掉移动输入，但保留 AI 的朝向意图（解除后立刻恢复）
        if (!canInput || !MotionCanMove)
        {
            SetMoveInput(Vector3.zero);
            if (anim != null) anim.Move(false);
            moveState.yawSpeed = 0f;
            return;
        }

        bool blockForward = InputBlocked(InputBlockOp.Forward);
        bool blockRotation = InputBlocked(InputBlockOp.Rotation);

        Vector3 dir = ResolveNavDirection();
        if (dir.sqrMagnitude > 0.0001f)
        {
            if (!blockRotation) FaceDirection(dir); // 转向即时对准行进方向
            SetMoveInput(blockForward ? Vector3.zero : Vector3.forward);
            if (anim != null) anim.Move(!blockForward);
            return;
        }

        // 无方向：分两种情况处理，不能混用朝向，否则会在「行进方向」与「目标方向」之间来回翻
        SetMoveInput(Vector3.zero);
        if (anim != null) anim.Move(false);

        if (HasNavDestination)
        {
            // 还有目的地但已进入到达死区：保持当前朝向停住，不要再切去朝目标
            moveState.yawSpeed = 0f;
            return;
        }

        // 站桩（攻击档已 StopMoving）：朝 AI 指定的目标转
        if (robotFaceSet && !blockRotation)
        {
            // 目标几乎与自己重合时方向向量极短、逐帧乱跳，直接不转，否则会高速来回抽搐
            Vector3 toFace = robotFacePoint - transform.position;
            toFace.y = 0f;
            if (toFace.sqrMagnitude > 0.09f) FaceDirection(toFace);
            else moveState.yawSpeed = 0f;
        }
        else moveState.yawSpeed = 0f;
    }

    // AI 转向瞬时生效：不做渐转，客户端按同步包里的 yaw 绝对值直接对齐
    private const float RobotFaceDeadzone = 5f; // 方向抖动小于此角度不动朝向，避免逐帧反复翻转

    private void FaceDirection(Vector3 direction)
    {
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
        {
            moveState.yawSpeed = 0f;
            return;
        }

        float targetYaw = Quaternion.LookRotation(direction).eulerAngles.y;
        if (Mathf.Abs(Mathf.DeltaAngle(moveState.yaw, targetYaw)) < RobotFaceDeadzone)
        {
            moveState.yawSpeed = 0f;
            return;
        }

        moveState.yaw = targetYaw;
        moveState.yawSpeed = 0f;
    }
    #endregion

    #region//Local
    private void EnsureMoveState()
    {
        if (moveState != null) return;
        moveState = new MoveState { yaw = transform.eulerAngles.y };
    }
    #endregion
}
