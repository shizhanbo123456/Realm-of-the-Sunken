using System;

namespace Ros.Transport
{
    // 服务器 → 客户端：打击震颤。只发给当事人（攻击方 / 受击方各一条），不夹在 SCCombatLabel 里——
    // 飘字是给视野内所有人看的数字，震颤只与该次命中有关的两台客户端有关
    public struct SCHitFeel
    {
        public const int RoleAttacker = 0; // 我是这次命中的攻击方
        public const int RoleVictim = 1;   // 我是被命中的一方

        public int role;   // RoleAttacker / RoleVictim
        public int type;   // CombatLabelType，只有 Normal / Crit / Shield 会下发
    }

    public struct SCHitFeelSerializer
    {
        public static bool Serialize(SCHitFeel value, byte[] result, ref int indexStart)
        {
            if (!EnsInstance.enableBitTransform)
            {
                if (!IntSerializer.Serialize(value.role, result, ref indexStart)) return false;
                return IntSerializer.Serialize(value.type, result, ref indexStart);
            }

            // 位级：1+4 = 5 位
            BitField.SetBuffer(result, indexStart);
            BitField.WriteInt(value.role, 0, 1);
            BitField.WriteInt(value.type, 0, 15);
            indexStart += BitField.Flush();
            return true;
        }

        public static SCHitFeel Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!EnsInstance.enableBitTransform)
            {
                return new SCHitFeel()
                {
                    role = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    type = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                };
            }

            if (invalidIndex - indexStart < 1) throw new Exception("反序列化 SCHitFeel 失败：剩余数据不足");

            BitField.SetBuffer(data, indexStart);
            var r = new SCHitFeel();
            r.role = BitField.ReadInt(0, 1);
            r.type = BitField.ReadInt(0, 15);
            indexStart += BitField.ReadByteLength;
            return r;
        }
    }
}
