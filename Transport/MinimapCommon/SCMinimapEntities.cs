using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：一批常规实体（小地图定时同步与上帝视角拉取共用，条目即 SCMinimapEntity 全字段）
    // 单包条数由 MinimapCommonChunk.EntityCapacity 按配表实测控制；不做整轮拼装编号，客户端逐条按 id 落点位、超时清除
    public struct SCMinimapEntities
    {
        public List<SCMinimapEntity> entries;
    }

    public struct SCMinimapEntitiesSerializer
    {
        public static bool Serialize(SCMinimapEntities value, byte[] result, ref int indexStart)
        {
            int count = value.entries.Count;
            if (!ByteSerializer.Serialize((byte)count, result, ref indexStart)) return false;
            for (int i = 0; i < count; i++)
                if (!SCMinimapEntitySerializer.Serialize(value.entries[i], result, ref indexStart)) return false;
            return true;
        }

        public static SCMinimapEntities Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCMinimapEntities() { entries = new List<SCMinimapEntity>() };
            int count = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
                info.entries.Add(SCMinimapEntitySerializer.Deserialize(data, ref indexStart, invalidIndex));
            return info;
        }
    }
}
