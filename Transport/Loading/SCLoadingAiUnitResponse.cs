using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：AI 单位表（虚拟负数 clientId + 预分配实体 id + 名字 + 所属阵营）
    public struct SCLoadingAiUnitResponse
    {
        public List<LoadingUnitEntry> list;
    }

    public struct SCLoadingAiUnitResponseSerializer
    {
        public static bool Serialize(SCLoadingAiUnitResponse value, byte[] result, ref int indexStart)
        {
            int count = value.list.Count;
            if (!IntSerializer.Serialize(count, result, ref indexStart)) return false;
            foreach (var entry in value.list)
            {
                if (!LoadingUnitEntrySerializer.Serialize(entry, result, ref indexStart)) return false;
            }
            return true;
        }

        public static SCLoadingAiUnitResponse Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCLoadingAiUnitResponse { list = new List<LoadingUnitEntry>() };
            int count = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
            {
                info.list.Add(LoadingUnitEntrySerializer.Deserialize(data, ref indexStart, invalidIndex));
            }
            return info;
        }
    }
}
