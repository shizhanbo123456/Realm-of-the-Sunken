namespace Ros.Transport
{
    // 服务器 → 客户端：这一档的抽卡种子（伪随机数 / 档位 / 当时的幸运）。
    // valid = false 表示这一档没次数或抽不出来，客户端收起面板；客户端用这套种子算出的卡与服务端一致
    public struct SCCardSeed
    {
        public bool valid;
        public byte tier;
        public int luck;
        public int seed;
    }

    public struct SCCardSeedSerializer
    {
        public static bool Serialize(SCCardSeed value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value.valid, result, ref indexStart)) return false;
            if (!ByteSerializer.Serialize(value.tier, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.luck, result, ref indexStart)) return false;
            return IntSerializer.Serialize(value.seed, result, ref indexStart);
        }

        public static SCCardSeed Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new SCCardSeed()
            {
                valid = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                tier = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex),
                luck = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                seed = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
