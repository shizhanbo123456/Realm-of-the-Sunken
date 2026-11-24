namespace Ros.Transport
{
    // 客户端 → 服务器：上报大地图展开状态（打开 / 关闭），打开期间服务器对该客户端不做雷达半径过滤、全量下发
    public struct CSMinimapBigMap
    {
        public bool open;
    }

    public struct CSMinimapBigMapSerializer
    {
        public static bool Serialize(CSMinimapBigMap value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
                return BoolSerializer.Serialize(value.open, result, ref indexStart);

            if (result.Length - indexStart < 1) return false;
            BitField.SetBuffer(result, indexStart);
            BitField.WriteBool(value.open);
            indexStart += BitField.Flush();
            return true;
        }

        public static CSMinimapBigMap Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
                return new CSMinimapBigMap() { open = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex) };

            BitField.SetBuffer(data, indexStart);
            var info = new CSMinimapBigMap() { open = BitField.ReadBool() };
            indexStart += BitField.ReadByteLength;
            return info;
        }
    }
}
