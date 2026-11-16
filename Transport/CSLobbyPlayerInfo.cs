namespace Ros.Transport
{
    public class CSLobbyPlayerInfo
    {
        public EntityType attackCharacter;
        public EntityType defenseCharacter;
        public string name = "";
        public bool aiTakeover; // 该客户端是否由 AI 接管（Home 阶段选定，进房后不可改）
    }

    public struct CSLobbyPlayerInfoSerializer
    {
        public static bool Serialize(CSLobbyPlayerInfo value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;
            if (!EntityTypeSerializer.Serialize(value.attackCharacter, result, ref indexStart)) return false;
            if (!EntityTypeSerializer.Serialize(value.defenseCharacter, result, ref indexStart)) return false;
            if (!StringSerializer.Serialize(value.name, result, ref indexStart)) return false;
            return BoolSerializer.Serialize(value.aiTakeover, result, ref indexStart);
        }

        public static CSLobbyPlayerInfo Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            var info = new CSLobbyPlayerInfo()
            {
                attackCharacter = EntityTypeSerializer.Deserialize(data, ref indexStart, invalidIndex),
                defenseCharacter = EntityTypeSerializer.Deserialize(data, ref indexStart, invalidIndex),
                name = StringSerializer.Deserialize(data, ref indexStart, invalidIndex),
                aiTakeover = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
            return info;
        }
    }
}
