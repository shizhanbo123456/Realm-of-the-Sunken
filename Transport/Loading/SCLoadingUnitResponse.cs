using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：一批参战单位（最多 16 条：16×(4 令牌+2 实体+4 阵营)+4 偏移+4 计数 = 168B）；offset 回显请求偏移
    public struct SCLoadingUnitResponse
    {
        public int offset;
        public List<LoadingUnitEntry> list;
    }

    public struct SCLoadingUnitResponseSerializer
    {
        public static bool Serialize(SCLoadingUnitResponse value, byte[] result, ref int indexStart)
        {
            if (!IntSerializer.Serialize(value.offset, result, ref indexStart)) return false;
            int count = value.list.Count;
            if (!IntSerializer.Serialize(count, result, ref indexStart)) return false;
            foreach (var entry in value.list)
            {
                if (!LoadingUnitEntrySerializer.Serialize(entry, result, ref indexStart)) return false;
            }
            return true;
        }

        public static SCLoadingUnitResponse Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCLoadingUnitResponse { list = new List<LoadingUnitEntry>() };
            info.offset = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            int count = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
            {
                info.list.Add(LoadingUnitEntrySerializer.Deserialize(data, ref indexStart, invalidIndex));
            }
            return info;
        }
    }
}
