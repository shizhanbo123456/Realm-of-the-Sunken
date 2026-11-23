using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：加载期全量玩家名字（真人 / 人机 / 观战方），人多时按每包 6 条分批下发
    public struct SCLoadingPlayerNames
    {
        public List<LoadingNameEntry> list;
    }

    public struct SCLoadingPlayerNamesSerializer
    {
        public static bool Serialize(SCLoadingPlayerNames value, byte[] result, ref int indexStart)
        {
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
            int count = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
            {
                info.list.Add(LoadingNameEntrySerializer.Deserialize(data, ref indexStart, invalidIndex));
            }
            return info;
        }
    }
}
