namespace Ros.Transport
{
    // 服务器 → 客户端：本人局内进度。等级/经验用于压制与升级抽卡，幸运决定卡池深度，
    // silver/gold/holo = 三档各自的待抽次数（服务器只下发次数，抽到什么由客户端算）
    public class SCPlayerProgress
    {
        public int level;
        public int exp;
        public int luck;
        public int silver;
        public int gold;
        public int holo;
    }

    public struct SCPlayerProgressSerializer
    {
        public static bool Serialize(SCPlayerProgress value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;
            if (!IntSerializer.Serialize(value.level, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.exp, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.luck, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.silver, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.gold, result, ref indexStart)) return false;
            return IntSerializer.Serialize(value.holo, result, ref indexStart);
        }

        public static SCPlayerProgress Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            return new SCPlayerProgress()
            {
                level = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                exp = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                luck = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                silver = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                gold = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                holo = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
