namespace Ros.Transport
{
    // 客户端 → 服务器：抽卡界面唯一的协议。hasChoice = false 只请求种子；
    // choice = 0/1/2 第几张卡、3 = 弃卡；requestSeed = 结算后是否还要这一档的下一个种子
    public struct CSCardAction
    {
        public const byte DiscardChoice = 3;

        public byte tier;
        public bool hasChoice;
        public byte choice;
        public bool requestSeed;
    }

    public struct CSCardActionSerializer
    {
        public static bool Serialize(CSCardAction value, byte[] result, ref int indexStart)
        {
            if (!ByteSerializer.Serialize(value.tier, result, ref indexStart)) return false;
            if (!BoolSerializer.Serialize(value.hasChoice, result, ref indexStart)) return false;
            if (!ByteSerializer.Serialize(value.choice, result, ref indexStart)) return false;
            return BoolSerializer.Serialize(value.requestSeed, result, ref indexStart);
        }

        public static CSCardAction Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSCardAction()
            {
                tier = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex),
                hasChoice = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                choice = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex),
                requestSeed = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
