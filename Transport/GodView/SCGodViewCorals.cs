using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：上帝视角区域内的一批珊瑚（数量最多，单独成流，只带 id 与坐标）
    // 单包条数由 GodViewChunk.CoralCapacity 写死（单包 ≤240B）；客户端逐条按 id 落点，超时清除
    public struct SCGodViewCorals
    {
        public struct Point
        {
            public ushort entityId;
            public float posX;
            public float posZ;
        }

        public List<Point> points;
    }

    public struct SCGodViewCoralsSerializer
    {
        private static int itemBytes;

        // 单条实际字节数（含非位级）：位级长度随配表量化变，容量计算不能写死
        public static int ItemBytes => itemBytes > 0 ? itemBytes : itemBytes = MeasureItemBytes();

        private static int MeasureItemBytes()
        {
            byte[] scratch = new byte[64];
            int index = 0;
            var probe = new SCGodViewCorals { points = new List<SCGodViewCorals.Point> { default } };
            if (!Serialize(probe, scratch, ref index))
                throw new System.Exception("测量 SCGodViewCorals 长度失败：缓冲区不足");
            return index - 1; // 首字节是包内条数
        }

        public static bool Serialize(SCGodViewCorals value, byte[] result, ref int indexStart)
        {
            int count = value.points.Count;
            if (!ByteSerializer.Serialize((byte)count, result, ref indexStart)) return false;
            if (!EnsInstance.enableBitTransform)
            {
                for (int i = 0; i < count; i++)
                {
                    var point = value.points[i];
                    if (!UshortSerializer.Serialize(point.entityId, result, ref indexStart)) return false;
                    if (!FloatSerializer.Serialize(point.posX, result, ref indexStart)) return false;
                    if (!FloatSerializer.Serialize(point.posZ, result, ref indexStart)) return false;
                }
                return true;
            }

            // 位级：坐标位宽按配表量化现算，每条字节数不写死
            for (int i = 0; i < count; i++)
            {
                var point = value.points[i];
                if (result.Length - indexStart < 1) return false;
                BitField.SetBuffer(result, indexStart);
                BitField.WriteInt(point.entityId, 0, SyncRanges.EntityIdMax);
                BitField.WriteFloat(point.posX, 0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                BitField.WriteFloat(point.posZ, 0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                indexStart += BitField.Flush();
            }
            return true;
        }

        public static SCGodViewCorals Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCGodViewCorals() { points = new List<SCGodViewCorals.Point>() };
            int count = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex);
            if (!EnsInstance.enableBitTransform)
            {
                for (int i = 0; i < count; i++)
                {
                    info.points.Add(new SCGodViewCorals.Point
                    {
                        entityId = UshortSerializer.Deserialize(data, ref indexStart, invalidIndex),
                        posX = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                        posZ = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    });
                }
                return info;
            }

            for (int i = 0; i < count; i++)
            {
                if (invalidIndex <= indexStart) throw new System.Exception("反序列化 SCGodViewCorals 失败：剩余数据不足");
                BitField.SetBuffer(data, indexStart);
                ushort entityId = (ushort)BitField.ReadInt(0, SyncRanges.EntityIdMax);
                float posX = BitField.ReadFloat(0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                float posZ = BitField.ReadFloat(0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                info.points.Add(new SCGodViewCorals.Point { entityId = entityId, posX = posX, posZ = posZ });
                indexStart += BitField.ReadByteLength;
            }
            return info;
        }
    }
}
