namespace Ros.Gm
{
    // GM 指令 id = gm_command 配表主键；配表增删或改 id 时同步改这里
    public static class GmCommandId
    {
        // 对局
        public const int TimeAdd = 0;
        public const int TimeReduce = 1;
        public const int TimeSet = 2;
        public const int End = 3;
        public const int SetDayTime = 4;
        public const int SetDayLength = 5;
        public const int SetNightLength = 6;

        // 实体
        public const int SpawnZombie = 7;
        public const int SpawnEliteZombie = 8;
        public const int SpawnAngelStatue = 9;
        public const int SpawnDevilStatue = 10;
        public const int SpawnTower = 11;

        // 玩家
        public const int Kick = 12;
        public const int Tp = 13;
        public const int TpTo = 14;
        public const int Respawn = 15;
        public const int GetSkill = 16;
        public const int UseSkill = 17;
        public const int ClearSkill = 18;
        public const int AddBuff = 19;
        public const int ClearBuff = 20;

        // 外挂
        public const int Fly = 21;
        public const int Invincible = 22;
        public const int ExtremeSpeed = 23;
        public const int ShowEnemyCampConversation = 24;

        public const int SettlementView = 25;

        // 服务器查询
        public const int ServerFps = 26;
        public const int ServerEntityCount = 27;
        public const int ServerTime = 28;
        public const int ServerDayLength = 29;
        public const int ServerNightLength = 30;
        public const int ServerZombieSpawnLevel = 31;

        // 客户端调试
        public const int Delay = 32;
        public const int DebugFps = 33;
        public const int DebugEntityCount = 34;
        public const int ClientTime = 35;
        public const int MeshSurfaceCount = 36;

        public const int Fog = 37;
    }
}
