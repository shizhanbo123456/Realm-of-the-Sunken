namespace Ros.Transport
{
    // 客户端 → 服务器：开始移动 / 移动中更新朝向。yaw = 角色要转过头并前进的绝对世界朝向（度，0~360）
    public struct CSMove
    {
        public float yaw;
    }

    public struct CSMoveSerializer
    {
        public static bool Serialize(CSMove value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
                return FloatSerializer.Serialize(value.yaw, result, ref indexStart);

            BitField.SetBuffer(result, indexStart);
            BitField.WriteFloat(value.yaw, 0f, SyncRanges.YawMax, SyncRanges.YawStep);
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
                };
            }

            BitField.SetBuffer(data, indexStart);
            var move = new CSMove()
            {
                yaw = BitField.ReadFloat(0f, SyncRanges.YawMax, SyncRanges.YawStep),
            };
            indexStart += BitField.ReadByteLength;
            return move;
        }
    }
}
