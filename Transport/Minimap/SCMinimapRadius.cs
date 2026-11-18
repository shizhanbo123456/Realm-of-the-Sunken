namespace Ros.Transport
{
    // 服务器 → 客户端：回应切换后的雷达显示半径（客户端据此调整裁剪与缩放）
    public struct SCMinimapRadius
    {
        public float radius;
    }

    public struct SCMinimapRadiusSerializer
    {
        public static bool Serialize(SCMinimapRadius value, byte[] result, ref int indexStart)
        {
            return FloatSerializer.Serialize(value.radius, result, ref indexStart);
        }

        public static SCMinimapRadius Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new SCMinimapRadius()
            {
                radius = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
