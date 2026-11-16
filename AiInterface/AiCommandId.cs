// AI 命令 id = ai_command 配表主键；配表增删或改 id 时同步改这里
public static class AiCommandId
{
    // Home
    public const int SetMyPlayerName = 0;
    public const int GetMyPlayerName = 1;
    public const int SelectCharacter = 2;
    public const int ConnectToServer = 3;
    public const int Takeover = 4;

    // Lobby
    public const int JoinCamp = 5;
    public const int LobbyPrepare = 6;
    public const int ExitRoom = 7;

    // 通讯
    public const int SendChatMessage = 8;
    public const int ReadChatMessage = 9;
    public const int QuickMessage = 10;
    public const int QuickMessageWithPosition = 11;

    // 战斗操作
    public const int MoveTo = 12;
    public const int CollectCoral = 13;
    public const int CastSkill = 14;
    public const int CalculatePath = 15;
    public const int StartLuaStateMachine = 16;
    public const int StopLuaStateMachine = 17;

    // 战斗查询
    public const int GetSkillList = 18;
    public const int SetSkillColumn = 19;
    public const int GetInfo = 20;
    public const int CurrentPos = 21;
    public const int GetSelfStatus = 22;
    public const int GetGuardProgress = 23;
    public const int GetMinimapEntities = 24;
    public const int GetDayNightInfo = 25;
    public const int GetCampCount = 26;
    public const int GetPointInfo = 27;
    public const int SetCameraRender = 28;
    public const int Screenshot = 29;

    // Lua 与枚举
    public const int ReloadLuaCommand = 30;
    public const int ExecLuaCommand = 31;
    public const int ListCommands = 32;
}
