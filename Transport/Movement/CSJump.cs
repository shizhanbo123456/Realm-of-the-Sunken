namespace Ros.Transport
{
    // 客户端 → 服务器：跳跃键（空格 / 左 Alt）的按下与抬起。down=true 起跳，飞行模式下转为上升长按
    public struct CSJump
    {
        public bool down;
    }

    public struct CSJumpSerializer
    {
        public static bool Serialize(CSJump value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
                return BoolSerializer.Serialize(value.down, result, ref indexStart);

            BitField.SetBuffer(result, indexStart);
            BitField.WriteBool(value.down);
            indexStart += BitField.Flush();
            return true;
        }

        public static CSJump Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
            {
                return new CSJump()
                {
                    down = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                };
            }

            BitField.SetBuffer(data, indexStart);
            var jump = new CSJump()
            {
                down = BitField.ReadBool(),
            };
            indexStart += BitField.ReadByteLength;
            return jump;
        }
    }
}
