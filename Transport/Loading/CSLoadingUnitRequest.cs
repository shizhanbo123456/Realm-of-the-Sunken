namespace Ros.Transport
{
    // 客户端 → 服务器：请求从 offset 开始的一批参战单位（真人 + 人机，各阵营混在一起）
    public struct CSLoadingUnitRequest
    {
        public int offset; // 已收到的单位条数；客户端每收一批就按新偏移再请求
    }

    public struct CSLoadingUnitRequestSerializer
    {
        public static bool Serialize(CSLoadingUnitRequest value, byte[] result, ref int indexStart)
        {
            return IntSerializer.Serialize(value.offset, result, ref indexStart);
        }

        public static CSLoadingUnitRequest Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSLoadingUnitRequest()
            {
                offset = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
