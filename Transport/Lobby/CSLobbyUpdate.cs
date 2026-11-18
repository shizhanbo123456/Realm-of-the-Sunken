namespace Ros.Transport
{
    public class CSLobbyUpdate
    {
        public int camp = -1;      // 0 进攻 / 1 防守 / 2 观战（-1 = 不变更）
        public bool ready;         // 准备状态
        public int attackRobotCount;   // 进攻方人机数量（人机 = 服务器侧虚拟玩家，不是 AI 接管）
        public int defenseRobotCount;  // 防守方人机数量
    }

    public struct CSLobbyUpdateSerializer
    {
        public static bool Serialize(CSLobbyUpdate value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;
            if (!IntSerializer.Serialize(value.camp, result, ref indexStart)) return false;
            if (!BoolSerializer.Serialize(value.ready, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.attackRobotCount, result, ref indexStart)) return false;
            return IntSerializer.Serialize(value.defenseRobotCount, result, ref indexStart);
        }

        public static CSLobbyUpdate Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            return new CSLobbyUpdate()
            {
                camp = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                ready = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                attackRobotCount = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                defenseRobotCount = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
