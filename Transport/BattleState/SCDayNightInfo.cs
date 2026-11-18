namespace Ros.Transport
{
    public struct SCDayNightInfo
    {
        public float cycleTime;
        public float dayDuration;
        public float nightDuration;
    }

    public struct SCDayNightInfoSerializer
    {
        public static bool Serialize(SCDayNightInfo value, byte[] result, ref int indexStart)
        {
            if (!FloatSerializer.Serialize(value.cycleTime, result, ref indexStart)) return false;
            if (!FloatSerializer.Serialize(value.dayDuration, result, ref indexStart)) return false;
            return FloatSerializer.Serialize(value.nightDuration, result, ref indexStart);
        }

        public static SCDayNightInfo Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new SCDayNightInfo()
            {
                cycleTime = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                dayDuration = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                nightDuration = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
