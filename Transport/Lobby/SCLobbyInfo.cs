using System.Collections.Generic;

namespace Ros.Transport
{
    public struct SCLobbyInfo
    {
        // full = true：全量快照（进房 / 重连建立成员表）；false：增量，members 只含本次变化的成员
        public bool full;
        public List<LobbyMemberInfo> members;
        public int attackRobotCount;   // 进攻方人机数量（人机 = 服务器侧虚拟玩家，不是 AI 接管）
        public int defenseRobotCount;  // 防守方人机数量
        public bool battleStarted;

        public struct LobbyMemberInfo
        {
            public int clientId; // 玩家令牌（非连接 id）
            public int camp;       // 0 进攻 / 1 防守 / 2 观战（-1 仅协议兼容，进大厅即默认观战）
            public int characterIndex;
            public bool ready;         // 组队界面准备状态（全员准备后服务器倒计时开战）
            public bool removed;       // 增量包专用：该成员已离开房间
        }
    }

    public struct SCLobbyInfoSerializer
    {
        public static bool Serialize(SCLobbyInfo value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value.full, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.attackRobotCount, result, ref indexStart)) return false;
            if (!IntSerializer.Serialize(value.defenseRobotCount, result, ref indexStart)) return false;
            if (!BoolSerializer.Serialize(value.battleStarted, result, ref indexStart)) return false;

            int count = value.members.Count;
            if (!IntSerializer.Serialize(count, result, ref indexStart)) return false;
            foreach (var member in value.members)
            {
                if (!IntSerializer.Serialize(member.clientId, result, ref indexStart)) return false;
                if (!IntSerializer.Serialize(member.camp, result, ref indexStart)) return false;
                if (!IntSerializer.Serialize(member.characterIndex, result, ref indexStart)) return false;
                if (!BoolSerializer.Serialize(member.ready, result, ref indexStart)) return false;
                if (!BoolSerializer.Serialize(member.removed, result, ref indexStart)) return false;
            }
            return true;
        }

        public static SCLobbyInfo Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            var info = new SCLobbyInfo()
            {
                full = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                attackRobotCount = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                defenseRobotCount = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                battleStarted = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                members = new List<SCLobbyInfo.LobbyMemberInfo>(),
            };
            int count = IntSerializer.Deserialize(data, ref indexStart, invalidIndex);
            for (int i = 0; i < count; i++)
            {
                info.members.Add(new SCLobbyInfo.LobbyMemberInfo()
                {
                    clientId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    camp = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    characterIndex = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    ready = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                    removed = BoolSerializer.Deserialize(data, ref indexStart, invalidIndex),
                });
            }
            return info;
        }
    }
}
