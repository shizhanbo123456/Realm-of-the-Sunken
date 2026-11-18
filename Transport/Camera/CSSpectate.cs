namespace Ros.Transport
{
    // 客户端 → 服务器：观战请求。targetClientId = 目标玩家 id（须同阵营且存活）；
    // 传入非法 / 死亡 / 异阵营目标 = 退出观战
    public struct CSSpectate
    {
        public int targetClientId;
    }

    public struct CSSpectateSerializer
    {
        public static bool Serialize(CSSpectate value, byte[] result, ref int indexStart)
        {
            return IntSerializer.Serialize(value.targetClientId, result, ref indexStart);
        }

        public static CSSpectate Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSSpectate()
            {
                targetClientId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
