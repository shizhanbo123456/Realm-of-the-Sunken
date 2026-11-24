using System.Collections.Generic;

namespace Ros.Transport
{
    // 服务器 → 客户端：一批珊瑚 + 鲛人（数量最多的两类，单独成流省带宽；不含精英鲛人）
    // 只带 id、坐标与 1bit 类别（鲛人另带 3bit 外观变体）；单包条数由 MinimapCommonChunk.SwarmCapacity 控制
    // 小地图定时同步与上帝视角拉取共用；客户端逐条按 id 落点位、超时清除
    public struct SCMinimapSwarm
    {
        public struct Point
        {
            public ushort entityId;
            public float posX;
            public float posZ;
            public bool zombie;    // false = 珊瑚，true = 鲛人（外观变体见 zombieVariant）
            public int zombieVariant; // 鲛人外观变体（0~4），珊瑚时为 0
        }

        public List<Point> points;
    }

    public struct SCMinimapSwarmSerializer
    {
        private static int itemBytes;

        // 单条实际字节数（含非位级）：位级长度随配表量化变，容量计算不能写死
        public static int ItemBytes => itemBytes > 0 ? itemBytes : itemBytes = MeasureItemBytes();

        private static int MeasureItemBytes()
        {
            byte[] scratch = new byte[64];
            int index = 0;
            var probe = new SCMinimapSwarm { points = new List<SCMinimapSwarm.Point> { default } };
            if (!Serialize(probe, scratch, ref index))
                throw new System.Exception("测量 SCMinimapSwarm 长度失败：缓冲区不足");
            return index - 1; // 首字节是包内条数
        }

        public static bool Serialize(SCMinimapSwarm value, byte[] result, ref int indexStart)
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
                    if (!BoolSerializer.Serialize(point.zombie, result, ref indexStart)) return false;
                    if (!IntSerializer.Serialize(point.zombieVariant, result, ref indexStart)) return false;
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
                BitField.WriteBool(point.zombie);
                if (point.zombie) BitField.WriteInt(point.zombieVariant, 0, 7);
                indexStart += BitField.Flush();
            }
            return true;
        }

        public static SCMinimapSwarm Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCMinimapSwarm() { points = new List<SCMinimapSwarm.Point>() };
            int count = ByteSerializer.Deserialize(data, ref indexStart, invalidIndex);
            if (!EnsInstance.enableBitTransform)
            {
                for (int i = 0; i < count; i++)
                {
                    info.points.Add(new SCMinimapSwarm.Point
                    {
                        entityId = UshortSerializer.Deserialize(data, ref indexStart, invalidIndex),
                        posX = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                        posZ = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                        zombie = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                        zombieVariant = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    });
                }
                return info;
            }

            for (int i = 0; i < count; i++)
            {
                if (invalidIndex <= indexStart) throw new System.Exception("反序列化 SCMinimapSwarm 失败：剩余数据不足");
                BitField.SetBuffer(data, indexStart);
                var point = new SCMinimapSwarm.Point
                {
                    entityId = (ushort)BitField.ReadInt(0, SyncRanges.EntityIdMax),
                    posX = BitField.ReadFloat(0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep),
                    posZ = BitField.ReadFloat(0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep),
                    zombie = BitField.ReadBool(),
                };
                if (point.zombie) point.zombieVariant = BitField.ReadInt(0, 7);
                info.points.Add(point);
                indexStart += BitField.ReadByteLength;
            }
            return info;
        }
    }
}
