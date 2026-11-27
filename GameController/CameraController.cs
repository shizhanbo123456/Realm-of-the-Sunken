using Ros.Config;
using Ros.Transport;
using UnityEngine;

public enum CameraViewMode
{
    ThirdPerson,
    TopDown,
    Free,
}

public class CameraController : MonoBehaviour
{
    public Transform lookTarget;

    // 渲染用的相机组件（CameraController 挂在主相机 GameObject 上）
    public Camera WorldCamera { get; private set; }

    // 焦点启停控制的相机：自身 + 直接子物体上的全部 Camera
    private Camera[] focusCameras;

    // 相机插值收尾后回调：UI 在此用最终相机变换投影，避免与渲染差一帧导致震颤
    public event System.Action OnCameraUpdated;

    public Vector2 yRange = new Vector2(1f, 3f);
    public Vector2 zRange = new Vector2(3f, 8f);

    public float slowSmooth = 2f;
    public float fastSmooth = 10f;
    // 高度越界缓冲带半宽：带内线性插值快慢平滑，超出带才用快速，避免速率阶跃震颤
    public float smoothDistance = 1.5f;

    // 俯仰偏转平滑速率
    public float pitchSmooth = 10f;
    // 俯仰钳制范围（度，世界俯仰、0=水平）：TickPitch 里钳制目标角
    public Vector2 pitchClamp = new Vector2(-45f, 45f);
    
    public float xSmooth = 2f;

    // 自动取景：每 autoFrameInterval 遍历可见实体，把可见模型的俯仰跨度约束进视野；
    // 有越界时经 SetPitch 按周期续期修正，无越界时停止刷新、由保持时间自然到期回正
    public float autoFrameInterval = 0.2f;
    // 俯仰角自适应是否忽略玩家角色：忽略时看向敌人而非自身
    public bool autoPitchIgnoreSelf = true;

    // 俯视模式：相机相对跟随点的世界偏移（yaw 恒为 0），以及位置平滑速率
    public Vector3 topDownOffset = new Vector3(0f, 16f, -4f);
    public float topDownSmooth = 8f;

    // 自由视角（仅纯观战）：平移 / 升降速度与俯仰钳制范围（转向与俯仰由鼠标滑动驱动）
    public float freeMoveSpeed = 12f;
    public float freeVerticalSpeed = 8f;
    public Vector2 freePitchClamp = new Vector2(-89f, 89f);
    // 自由视角位置上报：每 freeReportInterval 秒检查一次，与上次上报位置水平距离小于 freeReportDistance 则不发
    public float freeReportInterval = 1f;
    public float freeReportDistance = 1f;

    // 视角震动：强度档位与形状参数（幅度上限 / roll / 衰减 / 噪声频率）全部读配表 camera_shake_*

    public CameraViewMode Mode { get; private set; } = CameraViewMode.ThirdPerson;

    public bool IsFree => Mode == CameraViewMode.Free;

    // 移动输入的方向基准：第三人称取镜头 yaw，俯视 / 自由视角取世界轴向
    public float MoveReferenceYaw => Mode == CameraViewMode.ThirdPerson ? camYaw : 0f;

    private float autoFrameTimer;

    // 第三人称镜头偏航（度）：只由鼠标水平滑动驱动，与角色朝向无关
    private float camYaw;

    // 俯仰偏转
    private float pitchTarget;
    private float pitchHoldTime;
    private bool lastAutoPitch; // 自动俯仰角刚被打开时丢一次保持期，让俯仰回到自动取景口径

    // 自由视角位姿
    private float freeYaw;
    private float freePitch;

    // 自由视角位置上报状态
    private float freeReportTimer;
    private Vector3 freeReportPos;
    private bool freeReportPending; // 进入自由视角后的首包（不等满一个上报周期）

    // 会话跟踪：新一局按设置面板的口径重建镜头模式
    private bool sessionActive;

    // 焦点启停开关的上次值：设置面板改动后即时应用，不必等下次启动
    private bool lastCameraActiveWithFocus;

    // 震动状态：trauma 累积到 1，位移取 trauma² 保证小伤害几乎不动
    private float trauma;
    private readonly float[] shakeSeed = new float[3];
    private Vector3 shakeBasePos;
    private Quaternion shakeBaseRot = Quaternion.identity;
    private Vector3 shakeAppliedPos;
    private Quaternion shakeAppliedRot = Quaternion.identity;
    private bool shakeBaseReady;

    private void Awake()
    {
        Tool.CameraController = this;
        WorldCamera = GetComponent<Camera>();
        CollectFocusCameras();
        lastCameraActiveWithFocus = ClientSettings.CameraActiveWithFocus;
        ApplyFocusState(Application.isFocused);
        camYaw = transform.eulerAngles.y;
        lastAutoPitch = ClientSettings.AutoPitch;
        for (int i = 0; i < shakeSeed.Length; i++) shakeSeed[i] = Random.value * 100f;
        ApplyCursorState();
    }

    // 自身与直接子物体的全部相机一起受焦点开关控制
    private void CollectFocusCameras()
    {
        var list = new System.Collections.Generic.List<Camera>();
        list.AddRange(GetComponents<Camera>());
        foreach (Transform child in transform) list.AddRange(child.GetComponents<Camera>());
        focusCameras = list.ToArray();
    }

    // 开关关闭时相机保持常开；开启时按焦点启停
    public void ApplyFocusState(bool focused)
    {
        bool enable = !ClientSettings.CameraActiveWithFocus || focused;
        for (int i = 0; i < focusCameras.Length; i++) focusCameras[i].enabled = enable;
    }

    private void OnApplicationFocus(bool focus)
    {
        ApplyFocusState(focus);
    }

    // 第三人称与自由视角都靠指针位移转向：锁定隐藏指针；俯视与会话外把鼠标交还界面
    private void ApplyCursorState()
    {
        bool locked = sessionActive && (Mode == CameraViewMode.ThirdPerson || Mode == CameraViewMode.Free);
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnDestroy()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    // 外部震源入口：强度档位由调用方按配表给（受击 / 命中），这里只负责累积与衰减
    public void AddTrauma(float amount)
    {
        if (amount <= 0f) return;
        trauma = Mathf.Clamp01(trauma + amount);
    }

    public void SetLookTarget(Transform target, bool snap = true)
    {
        lookTarget = target;
        if (target == null) return;

        // 自由视角不能被跟随点拽走：只记引用不摆位
        if (!snap || Mode == CameraViewMode.Free) return;
        if (Mode == CameraViewMode.TopDown)
        {
            SnapToTopDown();
        }
        else
        {
            camYaw = target.eulerAngles.y; // 绑定新角色时镜头回到角色正后方
            SnapToThirdPerson();
        }
        NotifyCameraUpdated();
    }

    // 瞬移到第三人称稳态位姿（绑定/换绑/从自由视角返回）
    private void SnapToThirdPerson()
    {
        Vector3 back = Quaternion.Euler(0f, camYaw, 0f) * Vector3.back;
        float yCenter = (yRange.x + yRange.y) * 0.5f;
        float zCenter = (zRange.x + zRange.y) * 0.5f;
        transform.position = lookTarget.position + Vector3.up * yCenter + back * zCenter;

        transform.LookAt(lookTarget.position);
        if (ClientSettings.AutoPitch) UpdateAutoPitch();
        TickPitch();
    }

    // 瞬移到俯视稳态位姿（绑定/换绑）：位置与朝向都取 TickTopDown 的收敛目标，否则下一帧会被平滑拉走
    private void SnapToTopDown()
    {
        transform.position = lookTarget.position + topDownOffset;
        transform.rotation = TopDownRotation();
    }

    // 俯视朝向：偏航恒为 0，俯角由相机与跟随点的相对位置推出
    private Quaternion TopDownRotation()
    {
        Vector3 d = lookTarget.position - transform.position;
        float flat = Mathf.Sqrt(d.x * d.x + d.z * d.z);
        float pitch = -Mathf.Atan2(d.y, flat) * Mathf.Rad2Deg;
        return Quaternion.Euler(pitch, 0f, 0f);
    }

    public void SetMode(CameraViewMode mode)
    {
        if (Mode == mode) return;
        bool fromFree = Mode == CameraViewMode.Free;
        Mode = mode;
        ApplyCursorState();

        if (mode == CameraViewMode.Free)
        {
            freeYaw = transform.eulerAngles.y;
            freePitch = NormalizePitch(transform.eulerAngles.x);
            freeReportPending = true; // 首包立刻发，避免进入后 1s 内服务器仍按原实体下发
            freeReportTimer = 0f;
            return;
        }
        // 第三人称 ⇄ 俯视交给各自的 Tick 平滑过渡；从自由视角回来必须瞬移，否则会横穿整张地图
        if (fromFree) SendFreeCameraExit(); // 通知服务器把视野锚点交还跟随实体
        pitchHoldTime = 0f; // 丢弃切换前的取景保持，回第三人称时直接按当前几何重算俯仰
        if (mode == CameraViewMode.ThirdPerson)
        {
            // 进第三人称时镜头先落到角色正后方，之后完全交给鼠标
            if (lookTarget != null) camYaw = lookTarget.eulerAngles.y;
            if (fromFree && lookTarget != null)
            {
                SnapToThirdPerson();
                NotifyCameraUpdated();
            }
        }
    }

    private static float NormalizePitch(float pitch)
    {
        return pitch > 180f ? pitch - 360f : pitch;
    }

    private void Update()
    {
        if (ClientSettings.CameraActiveWithFocus != lastCameraActiveWithFocus)
        {
            lastCameraActiveWithFocus = ClientSettings.CameraActiveWithFocus;
            ApplyFocusState(Application.isFocused);
        }

        var loading = ClientLoadingManager.Current;
        TickSession(loading);
        if (!sessionActive) return;

        bool spectator = loading.IsSpectator;
        bool keysBlocked = InputManager.PlayerInputBlocked; // 输入被 UI 占用或 AI 接管时相机也不收键

        if (!keysBlocked && spectator && Mode != CameraViewMode.Free && Input.GetKeyDown(KeyCode.W))
        {
            SetMode(CameraViewMode.Free); // 纯观战：按 W 脱离跟随进自由视角
        }

        if (!keysBlocked && Input.GetKeyDown(GlobalConfig.camera_mode_key))
        {
            switch (Mode)
            {
                case CameraViewMode.Free: SetMode(CameraViewMode.ThirdPerson); break;
                case CameraViewMode.ThirdPerson: SetMode(CameraViewMode.TopDown); break;
                case CameraViewMode.TopDown: SetMode(CameraViewMode.ThirdPerson); break;
            }
            ClientSettings.ThirdPersonCamera = Mode == CameraViewMode.ThirdPerson; // 与设置面板同一个开关
        }

        // 设置面板的勾选是模式口径：C 键切换时写回同一个值，两边永远一致
        if (Mode == CameraViewMode.ThirdPerson || Mode == CameraViewMode.TopDown)
        {
            var wanted = ClientSettings.ThirdPersonCamera ? CameraViewMode.ThirdPerson : CameraViewMode.TopDown;
            if (Mode != wanted) SetMode(wanted);
        }

        // 自动俯仰角刚打开：丢掉鼠标留下的保持期，让俯仰回到自动取景口径
        if (ClientSettings.AutoPitch != lastAutoPitch)
        {
            lastAutoPitch = ClientSettings.AutoPitch;
            pitchHoldTime = 0f;
        }

        if (Mode == CameraViewMode.Free) TickFreeLook();
        else TickMouseLook();
    }

    // 会话起止：按设置面板的勾选重建镜头模式（结束时一并离开自由视角，避免继续上报相机位置）
    private void TickSession(ClientLoadingManager loading)
    {
        bool active = loading != null && loading.HasSession;
        if (active == sessionActive) return;
        sessionActive = active;
        Mode = ClientSettings.ThirdPersonCamera ? CameraViewMode.ThirdPerson : CameraViewMode.TopDown;
        pitchHoldTime = 0f;
        ApplyCursorState();
    }

    // 第三人称：鼠标水平滑动转镜头，纵向滑动改俯仰（勾选自动俯仰角时不接管）；俯视不响应鼠标
    private void TickMouseLook()
    {
        if (Mode != CameraViewMode.ThirdPerson || InputManager.PlayerInputBlocked) return;

        // Mouse X/Y 轴的 sensitivity=0.1，乘 10 还原成像素口径，与原 mousePosition 差值同灵敏度
        float dx = Input.GetAxis("Mouse X") * 10f;
        float dy = Input.GetAxis("Mouse Y") * 10f;

        if (dx != 0f) camYaw = Mathf.Repeat(camYaw + dx * GlobalConfig.mouse_yaw_sensitivity, 360f);
        if (!ClientSettings.AutoPitch && dy != 0f)
            pitchTarget = Mathf.Clamp(pitchTarget - dy * GlobalConfig.mouse_pitch_sensitivity, pitchClamp.x, pitchClamp.y);
    }

    // 单轴平滑速率：带内 slow，带外 fast，过渡带内线性插值
    private float AxisRate(float v, Vector2 range, float smoothDistance, float slow, float fast)
    {
        if (v < range.x)
        {
            if (smoothDistance <= 0f || v <= range.x - smoothDistance) return fast;
            return Mathf.Lerp(fast, slow, (v - (range.x - smoothDistance)) / smoothDistance);
        }
        if (v > range.y)
        {
            if (smoothDistance <= 0f || v >= range.y + smoothDistance) return fast;
            return Mathf.Lerp(slow, fast, (v - range.y) / smoothDistance);
        }
        return slow;
    }

    private void LateUpdate()
    {
        RestoreShakeBase();

        if (Mode == CameraViewMode.Free)
        {
            TickFreeMove(Time.deltaTime);
            TickFreeReport(Time.deltaTime);
            ApplyShake();
            NotifyCameraUpdated();
            return;
        }
        // 跟随点缺失（本地死亡观战等）镜头不动，但要继续通知：世界层血条仍需按静止相机重投影
        if (lookTarget == null)
        {
            NotifyCameraUpdated();
            return;
        }

        if (Mode == CameraViewMode.TopDown)
        {
            TickTopDown(Time.deltaTime);
            ApplyShake();
            NotifyCameraUpdated();
            return;
        }

        TickThirdPerson(Time.deltaTime);

        ApplyShake();
        NotifyCameraUpdated();
    }

    private void TickThirdPerson(float deltaTime)
    {
        // 偏航不平滑：相机全程在镜头的正后方（camYaw 由鼠标决定，与角色朝向无关）
        float yaw = camYaw;
        Vector3 back = Quaternion.Euler(0f, yaw, 0f) * Vector3.back;
        Vector3 toCam = transform.position - lookTarget.position;
        toCam.y = 0f;
        float zCur = toCam.magnitude;
        float yCur = transform.position.y - lookTarget.position.y;

        // 半径/高度双轴弹性平滑（区间内慢速、越界快速）
        float zRate = AxisRate(zCur, zRange, smoothDistance, slowSmooth, fastSmooth);
        float yRate = AxisRate(yCur, yRange, smoothDistance, slowSmooth, fastSmooth);
        float zCenter = (zRange.x + zRange.y) * 0.5f;
        float yCenter = (yRange.x + yRange.y) * 0.5f;
        float zNew = Mathf.Lerp(zCur, zCenter, 1f - Mathf.Exp(-zRate * deltaTime));
        float yNew = Mathf.Lerp(yCur, yCenter, 1f - Mathf.Exp(-yRate * deltaTime));

        // 横向偏移按镜头自身右方向修正（角色转向不再带动相机）
        Vector3 camRight = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        float x = Vector3.Dot(lookTarget.position - transform.position, camRight);
        float xNew = Mathf.Lerp(x, 0, 1f - Mathf.Exp(-xSmooth * deltaTime));

        transform.position = lookTarget.position + Vector3.up * yNew + back * zNew - camRight * xNew;

        // 自动取景周期遍历（勾选自动俯仰角时才跑，否则俯仰归鼠标）
        if (ClientSettings.AutoPitch)
        {
            autoFrameTimer += deltaTime;
            if (autoFrameTimer >= autoFrameInterval)
            {
                autoFrameTimer = 0f;
                UpdateAutoPitch();
            }
        }
        TickPitch();
    }

    // 俯视：位置平滑追「跟随点 + 偏移」，朝向 LookAt 跟随点但偏航恒为 0（只保留俯角）
    private void TickTopDown(float deltaTime)
    {
        Vector3 target = lookTarget.position + topDownOffset;
        // 速率为 0 即视为不平滑（瞬时跟随），避免 1-Exp(-0) = 0 卡住不动
        float k = topDownSmooth > 0f ? 1f - Mathf.Exp(-topDownSmooth * deltaTime) : 1f;
        transform.position = Vector3.Lerp(transform.position, target, k);

        var targetRotation = TopDownRotation();
        transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, 1f - Mathf.Exp(-pitchSmooth * deltaTime));
    }

    #region 自由视角（仅纯观战）
    // 鼠标滑动转向：水平滑动改 yaw、纵向滑动改俯仰（灵敏度与第三人称同一组口径，指针锁定故不受屏幕边缘限制）
    private void TickFreeLook()
    {
        if (InputManager.PlayerInputBlocked) return;

        float dx = Input.GetAxis("Mouse X") * 10f;
        float dy = Input.GetAxis("Mouse Y") * 10f;

        if (dx != 0f) freeYaw = Mathf.Repeat(freeYaw + dx * GlobalConfig.mouse_yaw_sensitivity, 360f);
        if (dy != 0f)
            freePitch = Mathf.Clamp(freePitch - dy * GlobalConfig.mouse_pitch_sensitivity, freePitchClamp.x, freePitchClamp.y);
    }

    // 位移固定：W/S 前后、A/D 左右，方向只取 yaw 的水平面（抬头低头不会向上/下飞）；左 Shift 降、左 Alt / 空格 升
    private void TickFreeMove(float deltaTime)
    {
        if (InputManager.PlayerInputBlocked) return;
        float yawRad = freeYaw * Mathf.Deg2Rad;
        Vector3 fwd = new Vector3(Mathf.Sin(yawRad), 0f, Mathf.Cos(yawRad));
        Vector3 right = new Vector3(Mathf.Cos(yawRad), 0f, -Mathf.Sin(yawRad));

        Vector3 flat = Vector3.zero;
        if (Input.GetKey(KeyCode.W)) flat += fwd;
        if (Input.GetKey(KeyCode.S)) flat -= fwd;
        if (Input.GetKey(KeyCode.D)) flat += right;
        if (Input.GetKey(KeyCode.A)) flat -= right;
        if (flat.sqrMagnitude > 1e-6f) flat = flat.normalized * freeMoveSpeed;

        float vertical = 0f;
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.Space)) vertical += 1f;
        if (Input.GetKey(KeyCode.LeftShift)) vertical -= 1f;

        transform.position += (flat + Vector3.up * (vertical * freeVerticalSpeed)) * deltaTime;
        transform.rotation = Quaternion.Euler(freePitch, freeYaw, 0f);
    }

    // 服务器按上报位置下发该玩家周围的实体：位移不足阈值时省掉这一包，客户端继续沿用上一次的锚点
    private void TickFreeReport(float deltaTime)
    {
        freeReportTimer -= deltaTime;
        if (freeReportTimer > 0f) return;
        freeReportTimer = freeReportInterval;

        Vector3 pos = transform.position;
        if (!freeReportPending)
        {
            float dx = pos.x - freeReportPos.x, dz = pos.z - freeReportPos.z;
            if (dx * dx + dz * dz < freeReportDistance * freeReportDistance) return;
        }
        freeReportPending = false;
        freeReportPos = pos;
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendFreeCamera(new CSFreeCamera() { free = true, posX = pos.x, posZ = pos.z });
    }

    private void SendFreeCameraExit()
    {
        freeReportPending = false;
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendFreeCamera(new CSFreeCamera() { free = false });
    }
    #endregion

    // 自动取景：把所有可见模型的上下边界压进水平视野中间 80%，0 偏转够用时不干预（自然回正）
    // 装不下经 SetPitch 给最小修正并按周期续期；两侧都装不下取上下限平均
    private void UpdateAutoPitch()
    {
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players == null || WorldCamera == null || lookTarget == null) return;

        Vector3 camPos = transform.position;
        float vHalf = WorldCamera.fieldOfView * 0.5f;
        float hHalf = Mathf.Atan(Mathf.Tan(vHalf * Mathf.Deg2Rad) * WorldCamera.aspect) * Mathf.Rad2Deg;
        float bandTan = Mathf.Tan(hHalf * Mathf.Deg2Rad) * 0.8f; // 中间 80%：两侧各让出 10%

        Vector3 fwd = transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 0.0001f) return;
        fwd.Normalize();

        float pitchTop = float.MaxValue;    // 上限：最高的所需视线（数值最小，向下为正）
        float pitchBottom = float.MinValue; // 下限：最低的所需视线（数值最大）
        bool any = false;

        var loading = ClientLoadingManager.Current;
        foreach (var view in players.AllViews)
        {
            if (view == null) continue;
            if (autoPitchIgnoreSelf && loading != null && loading.HasSession
                && view.id == loading.OwnEntityId) continue; // 忽略玩家角色
            Vector3 p = view.transform.position;
            float dx = p.x - camPos.x;
            float dz = p.z - camPos.z;
            float dist = Mathf.Sqrt(dx * dx + dz * dz);
            if (dist < 0.05f) continue; // 与相机几乎同位置：水平视线角无定义
            // 必须在镜头前方（身后＝相机与角色之间或更远，全部跳过）
            if (dx * fwd.x + dz * fwd.z <= 0f) continue;

            // 俯仰近似为 0 的水平视野测试：只取水平视场中间 80% 的实体
            float angle = Vector2.SignedAngle(new Vector2(fwd.x, fwd.z), new Vector2(dx / dist, dz / dist));
            if (Mathf.Abs(angle) >= hHalf) continue;
            if (Mathf.Abs(Mathf.Tan(angle * Mathf.Deg2Rad)) > bandTan) continue;

            // 模型上下边界对应的俯仰角（向下为正）
            float pitchToTop = Mathf.Atan2(camPos.y - (p.y + view.modelInfo.yRange.y), dist) * Mathf.Rad2Deg;
            float pitchToBottom = Mathf.Atan2(camPos.y - (p.y + view.modelInfo.yRange.x), dist) * Mathf.Rad2Deg;
            if (pitchToTop < pitchTop) pitchTop = pitchToTop;
            if (pitchToBottom > pitchBottom) pitchBottom = pitchToBottom;
            any = true;
        }

        if (!any) return; // 本轮没有纳入取景的实体：不刷新，已持有的偏转到保持期结束自然回正

        // 0 偏转（正看锚点）时的视角中心俯仰
        Vector3 toAnchor = lookTarget.position - camPos;
        float anchorPitch = Mathf.Atan2(-toAnchor.y, new Vector2(toAnchor.x, toAnchor.z).magnitude) * Mathf.Rad2Deg;

        // 视角中心可行区间：上边缘 ≤ 上限 且 下边缘 ≥ 下限（均为世界俯仰角，与 pitchTarget 同口径）
        float fitLow = pitchBottom - vHalf;
        float fitHigh = pitchTop + vHalf;
        if (fitLow > fitHigh)
        {
            // 视场装不下：中心取上限与下限的平均值
            SetPitch((pitchTop + pitchBottom) * 0.5f, autoFrameInterval * 1.5f);
            return;
        }
        if (anchorPitch < fitLow) SetPitch(fitLow, autoFrameInterval * 1.5f); // 压低
        else if (anchorPitch > fitHigh) SetPitch(fitHigh, autoFrameInterval * 1.5f); // 抬起
        // 否则 0 偏转已完整覆盖：不刷新，自然回正
    }

    // 俯仰目标（世界俯仰角，度）：保持 duration 秒后自动回归看向锚点；重复调用刷新保持时间
    private void SetPitch(float pitch, float duration = 0.1f)
    {
        pitchTarget = pitch;
        pitchHoldTime = duration;
    }

    // 第三人称俯仰：勾选自动俯仰角时由自动取景给目标并自然回正；取消勾选时目标完全由鼠标累加
    private void TickPitch()
    {
        if (ClientSettings.AutoPitch)
        {
            if (pitchHoldTime > 0f)
            {
                pitchHoldTime -= Time.deltaTime;
            }
            else
            {
                var d = lookTarget.position - transform.position;
                float dh = d.y;
                float ds = Mathf.Sqrt(d.x * d.x + d.z * d.z);
                pitchTarget = -Mathf.Atan(dh / ds) * Mathf.Rad2Deg;
            }
            pitchTarget = Mathf.Clamp(pitchTarget, pitchClamp.x, pitchClamp.y);
        }

        var eular = new Vector3(pitchTarget, camYaw, 0f);
        var targetQuaternion = Quaternion.Euler(eular);
        transform.rotation = Quaternion.Lerp(transform.rotation, targetQuaternion, 1f - Mathf.Exp(-pitchSmooth * Time.deltaTime));
    }

    // 把上帧叠加的震动偏移退回去，让平滑逻辑读到未震动的基准位姿；期间被外部瞬移改写则直接放弃回退
    private void RestoreShakeBase()
    {
        if (!shakeBaseReady) return;
        shakeBaseReady = false;
        if (transform.position != shakeAppliedPos) return;
        if (transform.rotation != shakeAppliedRot) return;
        transform.position = shakeBasePos;
        transform.rotation = shakeBaseRot;
    }

    // 在最终位姿之上叠加震动：偏移按镜头自身的 right/up 给，roll 绕视线轴；渲染与 UI 投影都用这个位姿
    private void ApplyShake()
    {
        if (trauma > 0f)
        {
            float decay = GlobalVariable.F(global_variablesEnum.camera_shake_decay);
            trauma = Mathf.Max(0f, trauma - decay * Time.unscaledDeltaTime);
        }

        shakeBasePos = transform.position;
        shakeBaseRot = transform.rotation;
        shakeBaseReady = true;

        if (trauma <= 0f)
        {
            shakeAppliedPos = shakeBasePos;
            shakeAppliedRot = shakeBaseRot;
            return;
        }

        float strength = trauma * trauma;
        float t = Time.unscaledTime * GlobalVariable.F(global_variablesEnum.camera_shake_frequency);
        float nx = Mathf.PerlinNoise(shakeSeed[0], t) * 2f - 1f;
        float ny = Mathf.PerlinNoise(shakeSeed[1], t) * 2f - 1f;
        float nr = Mathf.PerlinNoise(shakeSeed[2], t) * 2f - 1f;
        float maxOffset = Mode == CameraViewMode.TopDown
            ? GlobalVariable.F(global_variablesEnum.camera_shake_offset_top_down)
            : GlobalVariable.F(global_variablesEnum.camera_shake_offset_third_person);
        float maxRoll = GlobalVariable.F(global_variablesEnum.camera_shake_max_roll);

        transform.position = shakeBasePos + (transform.right * nx + transform.up * ny) * (maxOffset * strength);
        transform.rotation = shakeBaseRot * Quaternion.Euler(0f, 0f, nr * maxRoll * strength);

        shakeAppliedPos = transform.position;
        shakeAppliedRot = transform.rotation;
    }

    // Unity 要到渲染才重算 worldToCameraMatrix，同帧 WorldToScreenPoint 会用上一帧矩阵（血条/名字差一帧震颤）
    // 故改完变换后手动重置，让随后的投影回调拿到本帧相机
    private void NotifyCameraUpdated()
    {
        if (WorldCamera != null) WorldCamera.ResetWorldToCameraMatrix();
        if (OnCameraUpdated != null) OnCameraUpdated();
    }
}
