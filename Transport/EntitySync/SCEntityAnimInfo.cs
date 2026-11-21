using System;

using UnityEngine;

namespace Ros.Transport
{
    public struct SCEntityAnimInfo
    {
        public ushort entityId;
        public int animId;
        public float animFrame;
        public AnimParamPack animParams;
        public float moveSpeedScale;
        public bool paused;
    }

    public struct SCEntityAnimInfoSerializer
    {
        public static bool Serialize(SCEntityAnimInfo value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
            {
                if (!UshortSerializer.Serialize(value.entityId, result, ref indexStart)) return false;
                if (!IntSerializer.Serialize(value.animId, result, ref indexStart)) return false;
                if (!FloatSerializer.Serialize(value.animFrame, result, ref indexStart)) return false;
                if (!AnimParamPackSerializer.Serialize(value.animParams, result, ref indexStart)) return false;
                if (!FloatSerializer.Serialize(value.moveSpeedScale, result, ref indexStart)) return false;
                if (!BoolSerializer.Serialize(value.paused, result, ref indexStart)) return false;
                return true;
            }
            else
            {
                if (result.Length - indexStart < 1) return false;

                // 位级：字段位宽按各自范围现算，总字节数不写死
                BitField.SetBuffer(result, indexStart);
                BitField.WriteInt(value.entityId, 0, SyncRanges.EntityIdMax);
                BitField.WriteInt(value.animId, int.MinValue, int.MaxValue);   // 动画状态 fullPathHash，可为负
                BitField.WriteFloat(value.animFrame, 0f, 1f, 0.001f);          // 归一化播放进度
                BitField.WriteInt(value.animParams.characterType, 0, 3);
                BitField.WriteInt(value.animParams.attackId, 0, 63);
                BitField.WriteBool(value.animParams.inAir);
                BitField.WriteBool(value.animParams.moving);
                BitField.WriteBool(value.animParams.slide);
                BitField.WriteInt(value.animParams.hit, 0, 1);                 // 0 短受击 / 1 长受击
                BitField.WriteFloat(value.moveSpeedScale, 0f, SyncRanges.MoveSpeedScaleMax, 0.01f);
                BitField.WriteBool(value.paused);
                indexStart += BitField.Flush();
                return true;
            }
        }

        public static SCEntityAnimInfo Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
            {
                var info = new SCEntityAnimInfo()
                {
                    entityId = UshortSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    animId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    animFrame = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    animParams = AnimParamPackSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    moveSpeedScale = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    paused = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                };
                return info;
            }
            else
            {
                if (invalidIndex <= indexStart) throw new Exception("反序列化 SCEntityAnimInfo 失败：剩余数据不足");

                BitField.SetBuffer(data, indexStart);
                var r = new SCEntityAnimInfo();
                r.entityId = (ushort)BitField.ReadInt(0, SyncRanges.EntityIdMax);
                r.animId = BitField.ReadInt(int.MinValue, int.MaxValue);
                r.animFrame = BitField.ReadFloat(0f, 1f, 0.001f);
                r.animParams = new AnimParamPack() { characterType = BitField.ReadInt(0, 3), attackId = BitField.ReadInt(0, 63), inAir = BitField.ReadBool(), moving = BitField.ReadBool(), slide = BitField.ReadBool(), hit = BitField.ReadInt(0, 1) };
                r.moveSpeedScale = BitField.ReadFloat(0f, SyncRanges.MoveSpeedScaleMax, 0.01f);
                r.paused = BitField.ReadBool();
                indexStart += BitField.ReadByteLength;
                return r;
            }
        }
    }
}
