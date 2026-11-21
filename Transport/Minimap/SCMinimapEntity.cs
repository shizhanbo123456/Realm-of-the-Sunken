using System;

namespace Ros.Transport
{
    // 服务器 → 客户端：小地图上单个单位（每个实体一个独立数据包，不拼装、不编号、无标志位）
    // 直接承载实体字段，不再嵌套 MinimapEntity，省去一层对象分配与 GC
    public struct SCMinimapEntity
    {
        public ushort entityId;   // 实体 id（客户端据此定位本地玩家自己）
        public EntityType type;   // 实体类型（UI 按类别取图标 / 尺寸）
        public EntityCamp camp;   // 阵营（UI 着色：己方 / 敌方 / 中立）
        public float posX;        // 世界坐标 X
        public float posZ;        // 世界坐标 Z
        public bool marked;       // 是否被「白眼标记」（UI 高亮）
    }

    public struct SCMinimapEntitySerializer
    {
        private static int itemBytes;

        // 单条实际字节数（含非位级）：位级长度随配表量化变，容量计算不能写死
        public static int ItemBytes => itemBytes > 0 ? itemBytes : itemBytes = MeasureItemBytes();

        private static int MeasureItemBytes()
        {
            byte[] scratch = new byte[64];
            int index = 0;
            if (!Serialize(default, scratch, ref index))
                throw new System.Exception("测量 SCMinimapEntity 长度失败：缓冲区不足");
            return index;
        }

        public static bool Serialize(SCMinimapEntity value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
            {
                if (!UshortSerializer.Serialize(value.entityId, result, ref indexStart)) return false;
                if (!EntityTypeSerializer.Serialize(value.type, result, ref indexStart)) return false;
                if (!IntSerializer.Serialize((int)value.camp, result, ref indexStart)) return false;
                if (!FloatSerializer.Serialize(value.posX, result, ref indexStart)) return false;
                if (!FloatSerializer.Serialize(value.posZ, result, ref indexStart)) return false;
                if (!BoolSerializer.Serialize(value.marked, result, ref indexStart)) return false;
                return true;
            }
            else
            {
                if (result.Length - indexStart < 1) return false;

                // 位级：字段位宽按配表量化现算（含坐标），总字节数不写死
                BitField.SetBuffer(result, indexStart);
                BitField.WriteInt(value.entityId, 0, SyncRanges.EntityIdMax);
                BitField.WriteInt((int)value.type.category, 0, 7);
                BitField.WriteInt(value.type.value, 0, 31);
                BitField.WriteInt((int)value.camp, 0, 31);
                BitField.WriteFloat(value.posX, 0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                BitField.WriteFloat(value.posZ, 0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                BitField.WriteBool(value.marked);
                indexStart += BitField.Flush();
                return true;
            }
        }

        public static SCMinimapEntity Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
            {
                return new SCMinimapEntity()
                {
                    entityId = UshortSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    type = EntityTypeSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    camp = (EntityCamp)IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    posX = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    posZ = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    marked = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                };
            }
            else
            {
                if (invalidIndex <= indexStart) throw new Exception("反序列化 SCMinimapEntity 失败：剩余数据不足");

                BitField.SetBuffer(data, indexStart);
                var r = new SCMinimapEntity();
                r.entityId = (ushort)BitField.ReadInt(0, SyncRanges.EntityIdMax);
                r.type = new EntityType((EntityCategory)BitField.ReadInt(0, 7), BitField.ReadInt(0, 31));
                r.camp = (EntityCamp)BitField.ReadInt(0, 31);
                r.posX = BitField.ReadFloat(0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                r.posZ = BitField.ReadFloat(0f, SyncRanges.PositionXZMax, SyncRanges.MinimapPositionStep);
                r.marked = BitField.ReadBool();
                indexStart += BitField.ReadByteLength;
                return r;
            }
        }
    }
}
