namespace Ros.Transport
{
    // 客户端 → 服务器：开始移动 / 移动中更新朝向。yaw = 角色要转过头并前进的绝对世界朝向（度，0~360）
    // newPress = 本包由按下 WASD 触发（方向变化补发与 0.1s 心跳重发均为 false）
    public struct CSMove
    {
        public float yaw;
        public bool newPress;
    }

    public struct CSMoveSerializer
    {
        public static bool Serialize(CSMove value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
            {
                if (!FloatSerializer.Serialize(value.yaw, result, ref indexStart)) return false;
                return BoolSerializer.Serialize(value.newPress, result, ref indexStart);
            }

            BitField.SetBuffer(result, indexStart);
            BitField.WriteFloat(value.yaw, 0f, SyncRanges.YawMax, SyncRanges.YawStep);
            BitField.WriteBool(value.newPress);
            indexStart += BitField.Flush();
            return true;
        }

        public static CSMove Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
            {
                return new CSMove()
                {
                    yaw = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    newPress = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                };
            }

            BitField.SetBuffer(data, indexStart);
            var move = new CSMove()
            {
                yaw = BitField.ReadFloat(0f, SyncRanges.YawMax, SyncRanges.YawStep),
                newPress = BitField.ReadBool(),
            };
            indexStart += BitField.ReadByteLength;
            return move;
        }
    }
}
