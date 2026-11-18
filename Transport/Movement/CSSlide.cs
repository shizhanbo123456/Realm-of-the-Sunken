namespace Ros.Transport
{
    // 客户端 → 服务器：滑铲键（左 Shift）的按下与抬起。down=true 进入滑铲，飞行模式下转为下降长按
    public struct CSSlide
    {
        public bool down;
    }

    public struct CSSlideSerializer
    {
        public static bool Serialize(CSSlide value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
                return BoolSerializer.Serialize(value.down, result, ref indexStart);

            BitField.SetBuffer(result, indexStart);
            BitField.WriteBool(value.down);
            indexStart += BitField.Flush();
            return true;
        }

        public static CSSlide Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
            {
                return new CSSlide()
                {
                    down = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                };
            }

            BitField.SetBuffer(data, indexStart);
            var slide = new CSSlide()
            {
                down = BitField.ReadBool(),
            };
            indexStart += BitField.ReadByteLength;
            return slide;
        }
    }
}
