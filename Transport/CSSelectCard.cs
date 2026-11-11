namespace Ros.Transport
{
    // 客户端 → 服务器：选了哪张卡。tier = 0 白银 / 1 黄金 / 2 炫彩；cardId = card_option.csv 主键
    // 技能卡额外带 skillId（客户端抽到的具体技能），服务器据此直接发技能
    public struct CSSelectCard
    {
        public byte tier;
        public int cardId;
        public int skillId;
    }

    public struct CSSelectCardSerializer
    {
        public static bool Serialize(CSSelectCard value, byte[] result, ref int indexStart)
        {
            if (!ByteSerializer.Serialize(value.tier, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.cardId, result, ref indexStart)) return false;
            return IntSerializer.Serialize(value.skillId, result, ref indexStart);
        }

        public static CSSelectCard Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSSelectCard()
            {
                tier = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex),
                cardId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                skillId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
