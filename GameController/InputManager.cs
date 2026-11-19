using FairyGUI;
using Ros.Config;
using UnityEngine;

// 本机控制模式：Home 阶段可切换，连接服务器时随 CSLobbyPlayerInfo 上报，连上后不允许再改
public enum PlayerControlMode
{
    Player,
    Ai,
}

public class InputManager : MonoBehaviour
{
    private bool inputBlocked;   // GM 面板 / 文字输入期间屏蔽上行；进入屏蔽的那一帧先把手上的操作全部松开
    private bool moveSent;       // 服务器当前是否处于移动状态（决定发 CSMove 还是 CSMoveStop）
    private float lastSentYaw;
    private float nextMoveSendTime; // 按住 WASD 期间下一次心跳重发的时刻
    private bool jumpHeld;       // 跳跃键（空格 / 左 Alt 合成一个）的按下态，飞行时是上升长按
    private bool slideHeld;      // 滑铲键的按下态，飞行时是下降长按

    private void Awake()
    {
        Tool.InputManager = this;
    }

    private void Update()
    {
        ApplyUiTouch();
        TickGmHotkey();
        SendInput();
    }

    #region GM 面板入口
    // 1 秒内连按 3 次 V 打开；面板已开、输入被 UI 占用或 AI 接管时不响应
    private const KeyCode GmPanelKey = KeyCode.V;
    private const int GmPanelPressCount = 3;
    private const float GmPanelPressWindow = 1f;
    private int gmPressCount;
    private float gmFirstPressTime;

    private void TickGmHotkey()
    {
        if (Tool.UIManager == null) return;
        if (Tool.UIManager.IsPageVisible(pageEnum.gm) || PlayerInputBlocked) { ResetGmPress(); return; }
        if (GRoot.inst != null && GRoot.inst.focus is GTextInput) return; // 正在编辑输入框时不响应，避免 V 误开
        if (!Input.GetKeyDown(GmPanelKey)) return;

        if (gmPressCount == 0 || Time.unscaledTime - gmFirstPressTime > GmPanelPressWindow)
        {
            gmPressCount = 1;
            gmFirstPressTime = Time.unscaledTime;
        }
        else gmPressCount++;

        if (gmPressCount < GmPanelPressCount) return;
        ResetGmPress();
        Tool.UIManager.ShowPage(pageEnum.gm);
    }

    private void ResetGmPress()
    {
        gmPressCount = 0;
        gmFirstPressTime = 0f;
    }
    #endregion

    public static PlayerControlMode Mode { get; private set; } = PlayerControlMode.Player;

    public static bool IsAiControl => Mode == PlayerControlMode.Ai;

    public static void SetControlMode(PlayerControlMode mode)
    {
        if (Mode == mode) return;
        Mode = mode;
        ApplyUiTouch();
    }

    // AI 接管期间 FGUI 不吃任何指针输入，解除后恢复；GRoot 迟到由 Update 每帧补（同值赋值无副作用）
    private static void ApplyUiTouch()
    {
        if (GRoot.inst == null || GRoot.inst.touchable != IsAiControl) return;
        GRoot.inst.touchable = !IsAiControl;
        if (IsAiControl && Stage.inst != null) Stage.inst.SetFocus(null, true); // 顺手收掉接管前留下的文字焦点
    }

    // 唯一的输入占用判定：UI 占用（GM 面板 / 打字 / 设置页）或 AI 接管期间，角色、相机、技能与快捷键都不收输入
    public static bool PlayerInputBlocked => InputCapturedByUi || IsAiControl;

    public static bool InputCapturedByUi => Tool.UIManager != null && Tool.UIManager.InputCaptured;

    private void SendInput()
    {
        if (Tool.NetworkManager == null) return;

        if (PlayerInputBlocked)
        {
            if (!inputBlocked)
            {
                inputBlocked = true;
                ReleaseAll(); // 否则服务器仍认为在走 / 在飞
            }
            return;
        }
        inputBlocked = false;

        TickMove();
        TickJump();
        TickSlide();
        TickMouse();
    }

    #region 键盘
    // 移动：WASD 在相机参照系里合成方向，朝向 = 该方向的世界 yaw
    // 上行时机：按下 WASD 那一帧、方向变化超阈值、按住期间每 move_resend_interval 一次心跳（服务器朝向以此为准）
    private void TickMove()
    {
        if (!HasBattleEntity())
        {
            if (!moveSent) return;
            moveSent = false;
            Tool.NetworkManager.SendMoveStop();
            return;
        }

        Vector2 axis = new Vector2(
            (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f),
            (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f));

        if (axis.sqrMagnitude < 0.0001f)
        {
            if (!moveSent) return;
            moveSent = false;
            Tool.NetworkManager.SendMoveStop();
            return;
        }

        float rad = CameraYaw() * Mathf.Deg2Rad;
        float x = axis.x * Mathf.Cos(rad) + axis.y * Mathf.Sin(rad);
        float z = -axis.x * Mathf.Sin(rad) + axis.y * Mathf.Cos(rad);
        float yaw = Mathf.Repeat(Mathf.Atan2(x, z) * Mathf.Rad2Deg, 360f);

        bool newPress = Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.A)
            || Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.D);
        bool turned = moveSent && Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, yaw)) >= GlobalConfig.move_yaw_resend_threshold;
        if (moveSent && !newPress && !turned && Time.time < nextMoveSendTime) return;

        moveSent = true;
        lastSentYaw = yaw;
        nextMoveSendTime = Time.time + GlobalConfig.move_resend_interval;
        Tool.NetworkManager.SendMove(yaw, newPress);
    }

    // 空格与左 Alt 合成一个跳跃键：两键任一下都算按下，全部松开才算抬起（飞行模式借用这个长按态上升）
    private void TickJump()
    {
        bool held = Input.GetKey(GlobalConfig.jump_key) || Input.GetKey(GlobalConfig.jump_key_alt);
        if (held == jumpHeld) return;
        jumpHeld = held;
        Tool.NetworkManager.SendJump(held);
    }

    private void TickSlide()
    {
        bool held = Input.GetKey(GlobalConfig.slide_key);
        if (held == slideHeld) return;
        slideHeld = held;
        Tool.NetworkManager.SendSlide(held);
    }

    // 移动的方向基准：第三人称取镜头 yaw，俯视固定世界轴向
    private static float CameraYaw()
    {
        var camera = Tool.CameraController;
        return camera != null ? camera.MoveReferenceYaw : 0f;
    }
    #endregion

    #region 鼠标
    // 左键普攻 / 右键放当前选中槽位 / 滚轮切槽位
    private void TickMouse()
    {
        // 滚轮不看指针位置：技能栏本身就在界面上，压着它滚是最自然的操作
        float wheel = Input.GetAxis("Mouse ScrollWheel");
        if (wheel > 0f) SelectSlot(-1);
        else if (wheel < 0f) SelectSlot(1);

        if (PointerOnUi()) return; // 压在可点击控件上时左右键交给界面

        if (Input.GetMouseButtonDown(0)) CastUnarmed();
        if (Input.GetMouseButtonDown(1)) CastSelectedSkill();
    }

    // 普攻：与技能键一致，只上行，动画状态与参数全部由服务器下发
    private static void CastUnarmed() => Skills?.CastUnarmed();

    private static void CastSelectedSkill() => Skills?.CastSelected();

    private static void SelectSlot(int delta) => Skills?.SelectSlotDelta(delta);

    private static ClientSkillManager Skills =>
        Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;

    // 指针是否压在可点击控件上：按钮 / 输入框 / 下拉 / 滑动条才算
    // 页面背景、全屏容器、技能栏条目不参与判定 —— 它们铺满屏幕，算进去就等于左右键永远出不了手
    private static bool PointerOnUi()
    {
        if (GRoot.inst == null || GRoot.inst.displayObject is not Container root) return false;
        Vector2 point = Input.mousePosition;
        point.y = Screen.height - point.y;
        var hit = root.HitTest(point, false);

        for (var obj = hit; obj != null; obj = obj.parent)
        {
            var owner = obj.gOwner;
            if (owner == null || owner == GRoot.inst) return false;
            if (owner is GButton || owner is GTextInput || owner is GComboBox || owner is GSlider)
            {
                // 铺满全屏的控件是遮罩/兜底点击区，不当成"压在 UI 上"
                bool fullScreen = owner.width >= GRoot.inst.width * 0.9f && owner.height >= GRoot.inst.height * 0.9f;
                return !fullScreen;
            }
        }
        return false;
    }
    #endregion

    // 有没有可操控的战斗实体：观战 / 未开局时不上行技能与移动
    private static bool HasBattleEntity()
    {
        var loading = ClientLoadingManager.Current;
        return loading != null && loading.HasSession && loading.OwnEntityId != 0;
    }

    // 进入屏蔽时把按住态全部松开，告诉服务器角色已松手
    private void ReleaseAll()
    {
        if (moveSent)
        {
            moveSent = false;
            Tool.NetworkManager.SendMoveStop();
        }
        if (jumpHeld)
        {
            jumpHeld = false;
            Tool.NetworkManager.SendJump(false);
        }
        if (slideHeld)
        {
            slideHeld = false;
            Tool.NetworkManager.SendSlide(false);
        }
    }
}
