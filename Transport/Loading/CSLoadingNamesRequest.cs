namespace Ros.Transport
{
    // 客户端 → 服务器：请求从 offset 开始的一批名字（加载期第 0 步）
    public struct CSLoadingNamesRequest
    {
        public int offset; // 已收到的名字条数；客户端每收一批就按新偏移再请求，缺多少补多少
    }

    public struct CSLoadingNamesRequestSerializer
    {
        public static bool Serialize(CSLoadingNamesRequest value, byte[] result, ref int indexStart)
        {
            return IntSerializer.Serialize(value.offset, result, ref indexStart);
        }

        public static CSLoadingNamesRequest Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSLoadingNamesRequest()
            {
                offset = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
