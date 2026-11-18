using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：上帝视角区域内的一批珊瑚（数量最多，单独成流，只带 id 与坐标）
    // 单包条数由 GodViewChunk.CoralCapacity 写死（单包 ≤240B）；客户端逐条按 id 落点，超时清除
    public class SCGodViewCorals
    {
        public struct Point
        {
            public ushort entityId;
            public float posX;
            public float posZ;
        }

        public List<Point> points = new();
    }

    public struct SCGodViewCoralsSerializer
    {
        public static bool Serialize(SCGodViewCorals value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;

            int count = value.points != null ? value.points.Count : 0;
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

            // 位级：15(id)+10+10=35 位/条 → 每条 5 字节
            for (int i = 0; i < count; i++)
            {
                var point = value.points[i];
                if (result.Length - indexStart < 5) return false;
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
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
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
                if (invalidIndex - indexStart < 5) throw new System.Exception("反序列化 SCGodViewCorals 失败：剩余数据不足");
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
