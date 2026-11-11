namespace Ros.Transport
{
    // 客户端 → 服务器：弃掉一次抽取机会，整组折算成经验（经验值读 global_variables 的 card_discard_exp_*）
    public struct CSDiscardCard
    {
        public byte tier;
    }

    public struct CSDiscardCardSerializer
    {
        public static bool Serialize(CSDiscardCard value, byte[] result, ref int indexStart)
        {
            return ByteSerializer.Serialize(value.tier, result, ref indexStart);
        }

        public static CSDiscardCard Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSDiscardCard()
            {
                tier = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
