using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：一批名字（最多 6 条，受单包 256B 限制）；offset 回显请求偏移
    public struct SCLoadingPlayerNames
    {
        public int offset;
        public List<LoadingNameEntry> list;
    }

    public struct SCLoadingPlayerNamesSerializer
    {
        public static bool Serialize(SCLoadingPlayerNames value, byte[] result, ref int indexStart)
        {
            if (!IntSerializer.Serialize(value.offset, result, ref indexStart)) return false;
            int count = value.list.Count;
            if (!IntSerializer.Serialize(count, result, ref indexStart)) return false;
            foreach (var entry in value.list)
            {
                if (!LoadingNameEntrySerializer.Serialize(entry, result, ref indexStart)) return false;
            }
            return true;
        }

        public static SCLoadingPlayerNames Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCLoadingPlayerNames { list = new List<LoadingNameEntry>() };
            info.offset = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            int count = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
            {
                info.list.Add(LoadingNameEntrySerializer.Deserialize(data, ref indexStart, invalidIndex));
            }
            return info;
        }
    }
}
