// 上帝视角分块容量：单包 ≤240B，按固定开销 16B（分隔符 / 长度 / 消息头 / RPC 头）算出来的条数
// 实体：位级 6B / 非位级 24B；珊瑚（id + 坐标）：位级 5B / 非位级 10B
// 条数是写死的常量，不在运行时按实际写入字节数攒包
public static class GodViewChunk
{
    private const int PacketBytes = 240;
    private const int FrameBytes = 16;

    public static int EntityCapacity => (PacketBytes - FrameBytes) / (EnsInstance.enableBitTransform ? 6 : 24); // 37 / 9
    public static int CoralCapacity => (PacketBytes - FrameBytes) / (EnsInstance.enableBitTransform ? 5 : 10);  // 44 / 22
    public const int MaxHeldSkills = 24; // 详情里未装备技能的条数上限（技能栏槽位另算）
}
