namespace Ros.Transport
{
    // gm指令载荷：type = 配表主键；i 装 int/bool，v0~v2 装 float 或 Vector2/Vector3 分量
    public struct CSGm
    {
        public int type;
        public int i;
        public float v0;
        public float v1;
        public float v2;
    }

    public struct CSGmSerializer
    {
        public static bool Serialize(CSGm value, byte[] result, ref int indexStart)
        {
            if (!IntSerializer.Serialize(value.type, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.i, result, ref indexStart)) return false;
            if (!FloatSerializer.Serialize(value.v0, result, ref indexStart)) return false;
            if (!FloatSerializer.Serialize(value.v1, result, ref indexStart)) return false;
            return FloatSerializer.Serialize(value.v2, result, ref indexStart);
        }

        public static CSGm Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSGm()
            {
                type = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                i = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                v0 = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                v1 = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                v2 = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
