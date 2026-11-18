namespace Ros.Transport
{
    // 服务器 → 客户端：四个守护点的当前血量与人数倍率，与实体可见性无关，值变更时下发
    public class SCDefensePointProgress
    {
        public int mainHealth;
        public int sub1Health;
        public int sub2Health;
        public int sub3Health;
        public float healthScale; // 开局生成时的人数倍率：上限 = entity.csv 生命 × 该倍率；0 = 守护点还没生成
    }

    public struct SCDefensePointProgressSerializer
    {
        public static bool Serialize(SCDefensePointProgress value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;
            if (!IntSerializer.Serialize(value.mainHealth, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.sub1Health, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.sub2Health, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.sub3Health, result, ref indexStart)) return false;
            return FloatSerializer.Serialize(value.healthScale, result, ref indexStart);
        }

        public static SCDefensePointProgress Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            return new SCDefensePointProgress()
            {
                mainHealth = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                sub1Health = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                sub2Health = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                sub3Health = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                healthScale = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
