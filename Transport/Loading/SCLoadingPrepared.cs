namespace Ros.Transport
{
    // 服务器 → 客户端：加载元数据（实体 id 已预分配）。客户端据此主动拉取各类数据
    public struct SCLoadingPrepared
    {
        public ushort ownEntityId;       // 预分配的本体实体 id；观战方为 0
        public EntityType characterType; // 本地玩家本局使用的角色（结算算角色经验要用）
        public float serverTime;         // 服务器时间基准，客户端据此校准本地时钟
        public int nameCount;            // 全量名字表条数（真人 + 人机 + 观战）：客户端按它分批拉完为止
    }

    public struct SCLoadingPreparedSerializer
    {
        public static bool Serialize(SCLoadingPrepared value, byte[] result, ref int indexStart)
        {
            if (!UshortSerializer.Serialize(value.ownEntityId, result, ref indexStart)) return false;
            if (!EntityTypeSerializer.Serialize(value.characterType, result, ref indexStart)) return false;
            if (!FloatSerializer.Serialize(value.serverTime, result, ref indexStart)) return false;
            return IntSerializer.Serialize(value.nameCount, result, ref indexStart);
        }

        public static SCLoadingPrepared Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new SCLoadingPrepared()
            {
                ownEntityId = UshortSerializer.Deserialize(data, ref indexStart, invalidIndex),
                characterType = EntityTypeSerializer.Deserialize(data, ref indexStart, invalidIndex),
                serverTime = FloatSerializer.Deserialize(data, ref indexStart, invalidIndex),
                nameCount = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
