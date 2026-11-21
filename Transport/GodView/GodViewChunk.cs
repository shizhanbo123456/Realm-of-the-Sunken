// 上帝视角分块容量：单包 ≤240B，按固定开销 16B（分隔符 / 长度 / 消息头 / RPC 头）算条数
// 单条字节数由序列化器按当前配表实测（位级长度随坐标量化变，不能写死）
public static class GodViewChunk
{
    private const int PacketBytes = 240;
    private const int FrameBytes = 16;

    public static int EntityCapacity => (PacketBytes - FrameBytes) / Ros.Transport.SCMinimapEntitySerializer.ItemBytes;
    public static int CoralCapacity => (PacketBytes - FrameBytes) / Ros.Transport.SCGodViewCoralsSerializer.ItemBytes;
    public const int MaxHeldSkills = 24; // 详情里未装备技能的条数上限（技能栏槽位另算）
}
