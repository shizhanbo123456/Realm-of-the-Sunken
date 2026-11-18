using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：上帝视角区域内的一批实体（不含珊瑚，珊瑚走 SCGodViewCorals）
    // 单包条数由 GodViewChunk.EntityCapacity 写死（单包 ≤240B）；不做整轮拼装，客户端逐条按 id 落点位
    public class SCGodViewEntities
    {
        public List<SCMinimapEntity> entries = new();
    }

    public struct SCGodViewEntitiesSerializer
    {
        public static bool Serialize(SCGodViewEntities value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;

            int count = value.entries != null ? value.entries.Count : 0;
            if (!ByteSerializer.Serialize((byte)count, result, ref indexStart)) return false;
            for (int i = 0; i < count; i++)
                if (!SCMinimapEntitySerializer.Serialize(value.entries[i], result, ref indexStart)) return false;
            return true;
        }

        public static SCGodViewEntities Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            var info = new SCGodViewEntities() { entries = new List<SCMinimapEntity>() };
            int count = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
            {
                var entry = SCMinimapEntitySerializer.Deserialize(data, ref indexStart, invalidIndex);
                if (entry == null) continue;
                info.entries.Add(entry);
            }
            return info;
        }
    }
}
