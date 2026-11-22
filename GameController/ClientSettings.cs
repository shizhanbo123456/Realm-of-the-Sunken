// 局内客户端的本地开关：设置面板勾选写入，相机与输入直接读，四项全量由 SaveManager 落盘
public static class ClientSettings
{
    // 勾选 = 第三人称，取消 = 俯视
    public static bool ThirdPersonCamera { get; set; }

    // 勾选 = 俯仰角交给自动取景，鼠标不控俯仰；取消 = 鼠标纵向滑动控制俯仰
    public static bool AutoPitch { get; set; } = true;

    // 勾选 = 场景体积光；取消 = 体积光组件停用
    public static bool VolumetricLight { get; set; } = true;

    // 勾选 = 全屏焦散；目前只有勾选态进存档，运行时还没有消费方（见 07-待办）
    public static bool Caustics { get; set; } = true;

    // 位串长度：面板每加一个开关就加一位，存档字段结构不用动
    public const int BitCount = 4;

    #region//Local
    public static string ToBits()
    {
        var sb = new System.Text.StringBuilder(BitCount);
        sb.Append(ThirdPersonCamera ? '1' : '0');
        sb.Append(AutoPitch ? '1' : '0');
        sb.Append(VolumetricLight ? '1' : '0');
        sb.Append(Caustics ? '1' : '0');
        return sb.ToString();
    }

    // 先整串校验再赋值：中途失败不留半套设置给后面重建存档用
    public static void FromBits(string bits)
    {
        if (bits == null || bits.Length != BitCount)
            throw new System.FormatException($"设置位串「{bits}」长度不是 {BitCount}");
        for (int i = 0; i < bits.Length; i++)
            if (bits[i] != '0' && bits[i] != '1')
                throw new System.FormatException($"设置位串「{bits}」第 {i} 位不是 0/1");

        ThirdPersonCamera = bits[0] == '1';
        AutoPitch = bits[1] == '1';
        VolumetricLight = bits[2] == '1';
        Caustics = bits[3] == '1';
    }

    // 新建存档的默认值：与上面四个属性的初始值保持一致
    public static void ResetToDefault()
    {
        ThirdPersonCamera = false;
        AutoPitch = true;
        VolumetricLight = true;
        Caustics = true;
    }
    #endregion
}
