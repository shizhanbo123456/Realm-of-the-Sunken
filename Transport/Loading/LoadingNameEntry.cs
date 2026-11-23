namespace Ros.Transport
{
    // 加载期名字表条目：令牌 → 名字。只提供名字，玩家↔实体关系仍由 LoadingUnitEntry 提供
    public struct LoadingNameEntry
    {
        public int clientId; // 玩家令牌（非连接 id），人机为虚拟负数令牌
        public string name;
    }

    public struct LoadingNameEntrySerializer
    {
        public static bool Serialize(LoadingNameEntry value, byte[] result, ref int indexStart)
        {
            if (!IntSerializer.Serialize(value.clientId, result, ref indexStart)) return false;
            return StringSerializer.Serialize(value.name, result, ref indexStart);
        }

        public static LoadingNameEntry Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new LoadingNameEntry()
            {
                clientId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                name = StringSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
