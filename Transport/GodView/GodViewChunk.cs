// 上帝视角分块容量：常规实体流与珊瑚/鲛人流已下沉为小地图共用协议，容量统一在 MinimapCommonChunk
public static class GodViewChunk
{
    public static int EntityCapacity => Ros.Transport.MinimapCommonChunk.EntityCapacity;
    public static int SwarmCapacity => Ros.Transport.MinimapCommonChunk.SwarmCapacity;
    public const int MaxHeldSkills = 24; // 详情里未装备技能的条数上限（技能栏槽位另算）
}
