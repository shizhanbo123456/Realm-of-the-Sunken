namespace Ros.Transport
{
    // gm查询回包：type 回显指令；i 装 int 结果，v0 装 float 结果
    public class SCGm
    {
        public int type;
        public int i;
        public float v0;
    }

    public struct SCGmSerializer
    {
        public static bool Serialize(SCGm value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;
            if (!IntSerializer.Serialize(value.type, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.i, result, ref indexStart)) return false;
            return FloatSerializer.Serialize(value.v0, result, ref indexStart);
        }

        public static SCGm Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            return new SCGm()
            {
                type = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                i = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                v0 = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
