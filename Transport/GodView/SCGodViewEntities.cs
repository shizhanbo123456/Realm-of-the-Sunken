using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：上帝视角区域内的一批实体（不含珊瑚，珊瑚走 SCGodViewCorals）
    // 单包条数由 GodViewChunk.EntityCapacity 写死（单包 ≤240B）；不做整轮拼装，客户端逐条按 id 落点位
    public struct SCGodViewEntities
    {
        public List<SCMinimapEntity> entries;
    }

    public struct SCGodViewEntitiesSerializer
    {
        public static bool Serialize(SCGodViewEntities value, byte[] result, ref int indexStart)
        {
            int count = value.entries.Count;
            if (!ByteSerializer.Serialize((byte)count, result, ref indexStart)) return false;
            for (int i = 0; i < count; i++)
                if (!SCMinimapEntitySerializer.Serialize(value.entries[i], result, ref indexStart)) return false;
            return true;
        }

        public static SCGodViewEntities Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCGodViewEntities() { entries = new List<SCMinimapEntity>() };
            int count = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
                info.entries.Add(SCMinimapEntitySerializer.Deserialize(data, ref indexStart, invalidIndex));
            return info;
        }
    }
}
