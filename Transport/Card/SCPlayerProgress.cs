namespace Ros.Transport
{
    // 服务器 → 客户端：本人局内进度。等级/经验用于压制与升级抽卡，
    // silver/gold/holo = 三档各自的待抽次数（幸运随 SCCardSeed 下发，不在这里）
    public struct SCPlayerProgress
    {
        public int level;
        public int exp;
        public int silver;
        public int gold;
        public int holo;
    }

    public struct SCPlayerProgressSerializer
    {
        public static bool Serialize(SCPlayerProgress value, byte[] result, ref int indexStart)
        {
            if (!IntSerializer.Serialize(value.level, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.exp, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.silver, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.gold, result, ref indexStart)) return false;
            return IntSerializer.Serialize(value.holo, result, ref indexStart);
        }

        public static SCPlayerProgress Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new SCPlayerProgress()
            {
                level = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                exp = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                silver = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                gold = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                holo = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
