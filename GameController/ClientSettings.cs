// 局内客户端的本地开关：设置面板勾选写入，相机与输入直接读，跨局保留（不进存档）
public static class ClientSettings
{
    // 勾选 = 第三人称，取消 = 俯视
    public static bool ThirdPersonCamera { get; set; }

    // 勾选 = 俯仰角交给自动取景，鼠标不控俯仰；取消 = 鼠标纵向滑动控制俯仰
    public static bool AutoPitch { get; set; }

    // 勾选 = 受击/命中时镜头震动；取消 = 完全不震
    public static bool CameraShake { get; set; } = true;
}
