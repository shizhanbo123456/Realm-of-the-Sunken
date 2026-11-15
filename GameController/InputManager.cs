using FairyGUI;
using UnityEngine;

public class InputManager : MonoBehaviour
{
    private bool inputBlocked;   // GM 面板 / 文字输入期间屏蔽上行；进入屏蔽的那一帧先把手上的操作全部松开
    private bool moveSent;       // 服务器当前是否处于移动状态（决定发 CSMove 还是 CSMoveStop）
    private float lastSentYaw;
    private bool jumpHeld;       // 跳跃键（空格 / 左 Alt 合成一个）的按下态，飞行时是上升长按
    private bool slideHeld;      // 滑铲键的按下态，飞行时是下降长按

    private void Awake()
    {
        Tool.InputManager = this;
    }

    private void Update()
    {
        SendInput();
    }

    // 唯一的输入占用判定：UI 占用期间（GM 面板 / 文字输入 / 战斗页设置面板），角色、相机、技能与普攻都不再收输入
    public static bool InputCapturedByUi => Tool.UIManager != null && Tool.UIManager.InputCaptured;

    private void SendInput()
    {
        if (Tool.NetworkManager == null) return;

        if (InputCapturedByUi)
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
    // 移动：WASD 在相机参照系里合成方向，朝向 = 该方向的世界 yaw，移动中朝向变化超阈值才补发
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

        if (moveSent && Mathf.Abs(Mathf.DeltaAngle(lastSentYaw, yaw)) < GlobalConfig.move_yaw_resend_threshold) return;
        moveSent = true;
        lastSentYaw = yaw;
        Tool.NetworkManager.SendMove(yaw);
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
        if (wheel > 0f) SelectSlot(1);
        else if (wheel < 0f) SelectSlot(-1);

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
