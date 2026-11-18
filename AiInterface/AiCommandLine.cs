using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Ros.Config;
using Ros.Transport;
using TerrainGen;
using UnityEngine;
using UnityEngine.AI;

[Flags]
public enum AiPhase
{
    Offline = 1 << 0,
    Lobby = 1 << 1,
    Battle = 1 << 2,
}

public enum AiIntent
{
    Stand,
    MoveTo,
    AttackTarget,
    Stop,
}

public enum AiInterruptKind
{
    ChatMention,
    LuaTrigger,
    LuaError,
    BattleStart,
    BattleEnd,
    MoveFailed,
}

// 意图上行通道：协议接入前为 null，命令直接回「未接入」，不做假发送
public delegate bool AiIntentSender(AiIntent intent, Vector2 position, ushort targetId, out string error);

// 中断原因列表：有界、同类型合并、取走时整批清空
public sealed class AiInterruptHub
{
    private const int Capacity = 16;
    private const float MergeWindow = 2f;

    private sealed class Entry
    {
        public AiInterruptKind kind;
        public float time;
        public readonly List<string> fields = new();
        public int count = 1;
    }

    private readonly List<Entry> entries = new();
    private readonly StringBuilder builder = new();

    public void Push(AiInterruptKind kind, params string[] fields)
    {
        Entry target = null;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].kind != kind) continue;
            if (Time.time - entries[i].time > MergeWindow) break;
            target = entries[i];
            break;
        }

        if (target != null)
        {
            target.fields.Clear();
            if (fields != null) target.fields.AddRange(fields);
            target.time = Time.time;
            target.count++;
            return;
        }

        var entry = new Entry { kind = kind, time = Time.time };
        if (fields != null) entry.fields.AddRange(fields);
        entries.Add(entry);
        while (entries.Count > Capacity) entries.RemoveAt(0);
    }

    public bool TryDrain(out string text)
    {
        text = null;
        if (entries.Count == 0) return false;
        text = Build();
        entries.Clear();
        return true;
    }

    private string Build()
    {
        builder.Clear();
        for (int i = 0; i < entries.Count; i++)
        {
            if (i > 0) builder.Append('\n');
            var e = entries[i];
            builder.Append('[').Append(e.kind.ToString()).Append(']');
            for (int j = 0; j < e.fields.Count; j++)
            {
                builder.Append(j == 0 ? " " : " | ").Append(e.fields[j]);
            }
            if (e.count > 1) builder.Append(" ×").Append(e.count);
        }
        return builder.ToString();
    }
}

// AI 操控的命令行接口：命令表来自 ai_command 配表，解析 / 相位 / 参数校验后按主键分发
public sealed class AiCommandLine : ClientSubManager
{
    public static AiCommandLine Current { get; private set; }

    // 由意图协议层实现后赋值：返回 false 时 error 原样回给调用方
    public static AiIntentSender IntentSender;

    private sealed class Command
    {
        public int id;
        public string name;
        public AiPhase phase;
        public string usage;
        public ai_command row;
    }

    private struct ParamDef
    {
        public string name;
        public string type;
    }

    private struct ChatRecord
    {
        public int channel;
        public string kind;
        public string camp;
        public string name;
        public string content;
    }

    // 表里的参数已按类型解析好，handler 只用取值
    private sealed class AiArgs
    {
        private readonly object[] values;

        public AiArgs(object[] values) { this.values = values; }

        public int Int(int index) => (int)values[index];
        public float Float(int index) => (float)values[index];
        public bool Bool(int index) => (bool)values[index];
        public string Str(int index) => (string)values[index];
        public List<int> IntList(int index) => (List<int>)values[index];
    }

    private const int ChatLogCapacity = 200;
    private const int SystemChannel = 3;
    private const string ScreenshotFolder = "Screenshots";

    private readonly List<Command> commands = new();
    private readonly Dictionary<string, Command> byName = new();
    private bool registerWarned;
    private readonly List<ChatRecord> chatLog = new();
    private readonly List<string> selfBuffs = new();

    private readonly Dictionary<ushort, SCMinimapEntity> minimap = new();
    private readonly Dictionary<ushort, float> minimapSeen = new();
    private readonly List<ushort> minimapExpired = new();

    private readonly AiInterruptHub hub = new();

    private int selfHealth;
    private int selfMaxHealth;
    private SCDefensePointProgress guardProgress;

    private Task<NetworkManager.ConnectResult> connectTask;
    private Action<string> connectReply;
    private string connectIp;
    private bool lastReady; // 阵营与准备同包上报，改阵营时不能把准备状态清掉

    public AiInterruptHub Interrupts => hub;

    public int SelfHealth => selfHealth;

    public int SelfMaxHealth => selfMaxHealth;

    public IReadOnlyList<string> SelfBuffs => selfBuffs;

    public SCDefensePointProgress GuardSnapshot => guardProgress;

    public void CopyMinimap(List<SCMinimapEntity> buffer)
    {
        buffer.Clear();
        foreach (var pair in minimap) buffer.Add(pair.Value);
    }

    public override void Init(ClientLogicManager owner)
    {
        base.Init(owner);
        Current = this;
        IntentSender = SendIntentChannel;
        RegisterFromConfig();
    }

    // 意图通道自接线：命令行与 Lua 的意图都从这里上行
    private static bool SendIntentChannel(AiIntent intent, Vector2 position, ushort targetId, out string error)
    {
        if (!NetworkManager.CanSendWorldCommand)
        {
            error = "未联网";
            return false;
        }
        Tool.NetworkManager.SendAiIntent(new CSAiIntent
        {
            intent = (int)intent,
            posX = position.x,
            posZ = position.y,
            targetId = targetId,
        });
        error = null;
        return true;
    }

    // 服务器回执：目前只有移动失败（目的地算不出路）
    public void OnServerFeedback(SCAiFeedback info)
    {
        if (info.kind == 0)
        {
            hub.Push(AiInterruptKind.MoveFailed,
                "坐标=(" + info.posX.ToString("0.##") + "," + info.posZ.ToString("0.##") + ")",
                "目标=" + info.targetId);
            return;
        }
        Debug.LogError("[AI] 未知的 AI 回执类型：" + info.kind);
    }

    public override void Tick(float deltaTime)
    {
        TickMinimapExpire();
        TickConnect();
    }

    protected override void BindEvents()
    {
        EventManager.AddEvent<SCChat>(ClientEvent.OnChatMessage, OnChatMessage);
        EventManager.AddEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.AddEvent<int>(ClientEvent.OnShowPrompt, OnPrompt);
        EventManager.AddEvent<SCMinimapEntity>(ClientEvent.OnMinimapUpdate, OnMinimapUpdate);
        EventManager.AddEvent<SCEntityDisplayInfo>(ClientEvent.OnEntityDisplayUpdate, OnEntityDisplay);
        EventManager.AddEvent<SCDefensePointProgress>(ClientEvent.OnDefensePointProgress, OnGuardProgress);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.AddEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
    }

    protected override void UnbindEvents()
    {
        EventManager.RemoveEvent<SCChat>(ClientEvent.OnChatMessage, OnChatMessage);
        EventManager.RemoveEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.RemoveEvent<int>(ClientEvent.OnShowPrompt, OnPrompt);
        EventManager.RemoveEvent<SCMinimapEntity>(ClientEvent.OnMinimapUpdate, OnMinimapUpdate);
        EventManager.RemoveEvent<SCEntityDisplayInfo>(ClientEvent.OnEntityDisplayUpdate, OnEntityDisplay);
        EventManager.RemoveEvent<SCDefensePointProgress>(ClientEvent.OnDefensePointProgress, OnGuardProgress);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        if (Current == this) Current = null;
    }

    #region 对外入口
    public static AiPhase CurrentPhase
    {
        get
        {
            if (Tool.NetworkManager == null) return AiPhase.Offline;
            if (!NetworkManager.CanSendWorldCommand) return AiPhase.Offline;
            return NetworkManager.BattleRunning ? AiPhase.Battle : AiPhase.Lobby;
        }
    }

    public void Submit(string line, Action<string> onResult)
    {
        if (onResult == null) throw new ArgumentNullException(nameof(onResult));
        if (string.IsNullOrEmpty(line) || line.Trim().Length == 0)
        {
            onResult("错误：空命令");
            return;
        }
        if (commands.Count == 0) RegisterFromConfig();

        var tokens = SplitArgs(line);
        var cmd = Find(tokens[0]);
        if (cmd == null)
        {
            onResult($"错误：未知命令 {tokens[0]}（用 listCommands 查看）");
            return;
        }

        AiPhase phase = CurrentPhase;
        if ((cmd.phase & phase) == 0)
        {
            onResult($"错误：命令 {cmd.name} 在当前相位 {phase} 不可用");
            return;
        }

        var args = new string[tokens.Count - 1];
        for (int i = 0; i < args.Length; i++) args[i] = tokens[i + 1];

        if (!TryBuildArgs(cmd.row, args, out AiArgs parsed, out string error))
        {
            onResult(error);
            return;
        }
        Dispatch(cmd.id, parsed, onResult);
    }
    #endregion

    #region 注册（ai_command 配表驱动）
    private void RegisterFromConfig()
    {
        commands.Clear();
        byName.Clear();

        foreach (ai_command row in AiCommandConfig.ordered)
        {
            var cmd = new Command
            {
                id = row.key,
                name = row.command,
                phase = PhaseOf(row),
                usage = Usage(row),
                row = row,
            };
            commands.Add(cmd);
            byName[cmd.name] = cmd;
        }

        if (commands.Count == 0 && !registerWarned)
        {
            registerWarned = true;
            Debug.LogError("[AI] ai_command 配表为空：命令行没有注册任何命令");
        }
    }

    // 三列可用开关按位拼相位；全空 = 任何相位都不可用
    private static AiPhase PhaseOf(ai_command row)
    {
        AiPhase phase = 0;
        if (row.home == true) phase |= AiPhase.Offline;
        if (row.lobby == true) phase |= AiPhase.Lobby;
        if (row.battle == true) phase |= AiPhase.Battle;
        return phase;
    }

    private Command Find(string name)
    {
        return byName.TryGetValue(name, out var cmd) ? cmd : null;
    }

    private static List<ParamDef> Params(ai_command row)
    {
        var list = new List<ParamDef>(3);
        AddParam(list, row.param1Name, row.param1Type);
        AddParam(list, row.param2Name, row.param2Type);
        AddParam(list, row.param3Name, row.param3Type);
        return list;
    }

    private static void AddParam(List<ParamDef> list, string name, string type)
    {
        if (string.IsNullOrEmpty(name)) return;
        list.Add(new ParamDef { name = name, type = string.IsNullOrEmpty(type) ? "string" : type });
    }

    private static string Usage(ai_command row)
    {
        var builder = new StringBuilder(row.command);
        var parameters = Params(row);
        for (int i = 0; i < parameters.Count; i++)
        {
            builder.Append(' ').Append(parameters[i].name).Append(':').Append(parameters[i].type);
        }
        return builder.ToString();
    }
    #endregion

    #region 参数
    // 表里的最后一个 string（非首位）与 List<int> 吃掉剩余 token：聊天内容、lua 参数、技能列表都这样传
    private static bool TryBuildArgs(ai_command row, string[] tokens, out AiArgs args, out string error)
    {
        var parameters = Params(row);
        var values = new object[parameters.Count];
        int index = 0;

        for (int i = 0; i < parameters.Count; i++)
        {
            string type = parameters[i].type;

            if (type == "List<int>")
            {
                var list = new List<int>();
                for (; index < tokens.Length; index++)
                {
                    string[] parts = tokens[index].Split(';');
                    for (int p = 0; p < parts.Length; p++)
                    {
                        if (!int.TryParse(parts[p], NumberStyles.Integer, CultureInfo.InvariantCulture, out int item))
                        {
                            args = null;
                            error = $"错误：参数 {parameters[i].name} 必须是整数列表";
                            return false;
                        }
                        list.Add(item);
                    }
                }
                values[i] = list;
                continue;
            }

            if (type == "string" && i > 0)
            {
                var builder = new StringBuilder();
                for (; index < tokens.Length; index++)
                {
                    if (builder.Length > 0) builder.Append(' ');
                    builder.Append(tokens[index]);
                }
                values[i] = builder.ToString();
                continue;
            }

            if (index >= tokens.Length)
            {
                args = null;
                error = "错误：参数数量不符，用法 " + Usage(row);
                return false;
            }

            if (!TryParseScalar(type, tokens[index], out object value))
            {
                args = null;
                error = $"错误：参数 {parameters[i].name} 必须是 {type}";
                return false;
            }
            values[i] = value;
            index++;
        }

        if (index < tokens.Length)
        {
            args = null;
            error = "错误：参数数量不符，用法 " + Usage(row);
            return false;
        }

        args = new AiArgs(values);
        error = null;
        return true;
    }

    private static bool TryParseScalar(string type, string text, out object value)
    {
        switch (type)
        {
            case "int":
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
                {
                    value = i;
                    return true;
                }
                break;
            case "float":
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
                    && !float.IsNaN(f) && !float.IsInfinity(f))
                {
                    value = f;
                    return true;
                }
                break;
            case "bool":
                if (text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase))
                {
                    value = true;
                    return true;
                }
                if (text == "0" || string.Equals(text, "false", StringComparison.OrdinalIgnoreCase))
                {
                    value = false;
                    return true;
                }
                break;
            case "string":
                value = text;
                return true;
        }

        value = null;
        return false;
    }
    #endregion

    #region 分发
    private void Dispatch(int id, AiArgs a, Action<string> reply)
    {
        switch (id)
        {
            case AiCommandId.SetMyPlayerName: SetPlayerName(a, reply); break;
            case AiCommandId.GetMyPlayerName: GetPlayerName(reply); break;
            case AiCommandId.SelectCharacter: SelectCharacter(a, reply); break;
            case AiCommandId.ConnectToServer: Connect(a, reply); break;
            case AiCommandId.Takeover: Takeover(a, reply); break;
            case AiCommandId.JoinCamp: SelectCamp(a, reply); break;
            case AiCommandId.LobbyPrepare: SetReady(a, reply); break;
            case AiCommandId.ExitRoom: LeaveRoom(reply); break;
            case AiCommandId.SendChatMessage: SendChat(a, reply); break;
            case AiCommandId.ReadChatMessage: ReadChat(a, reply); break;
            case AiCommandId.QuickMessage: QuickMessage(a, reply); break;
            case AiCommandId.QuickMessageWithPosition: QuickMessageWithPosition(a, reply); break;
            case AiCommandId.MoveTo: MoveTo(a, reply); break;
            case AiCommandId.CollectCoral: AttackCoral(reply); break;
            case AiCommandId.CastSkill: CastSkill(a, reply); break;
            case AiCommandId.CalculatePath: PathLength(a, reply); break;
            case AiCommandId.StartLuaStateMachine: ActivateLua(reply); break;
            case AiCommandId.StopLuaStateMachine: StopLua(reply); break;
            case AiCommandId.GetSkillList: SkillList(reply); break;
            case AiCommandId.SetSkillColumn: SetSkillBar(a, reply); break;
            case AiCommandId.GetInfo: GetInfo(reply); break;
            case AiCommandId.CurrentPos: CurrentPos(reply); break;
            case AiCommandId.GetSelfStatus: SelfStatus(reply); break;
            case AiCommandId.GetGuardProgress: GuardProgress(reply); break;
            case AiCommandId.GetMinimapEntities: MinimapEntities(reply); break;
            case AiCommandId.GetDayNightInfo: DayNightInfo(reply); break;
            case AiCommandId.GetCampCount: CampCount(reply); break;
            case AiCommandId.GetPointInfo: PointInfo(reply); break;
            case AiCommandId.SetCameraRender: SetCameraRender(a, reply); break;
            case AiCommandId.Screenshot: Screenshot(reply); break;
            case AiCommandId.ReloadLuaCommand: ReloadLua(reply); break;
            case AiCommandId.ExecLuaCommand: RunLuaExtension(a, reply); break;
            case AiCommandId.ListCommands: ListCommands(reply); break;
            default:
                reply($"错误：命令 id={id} 没有对应的处理逻辑（配表新增了行但代码没接）");
                break;
        }
    }
    #endregion

    #region 通用命令
    private void SendChat(AiArgs a, Action<string> reply)
    {
        int channel = a.Int(0);
        if (channel < 0 || channel > 2)
        {
            reply("错误：channel 只能是 0 全频道 / 1 阵营 / 2 附近");
            return;
        }
        string content = a.Str(1);
        if (content.Length == 0)
        {
            reply("错误：聊天内容不能为空");
            return;
        }
        if (!NetworkManager.CanSendWorldCommand)
        {
            reply("错误：未联网");
            return;
        }

        Tool.NetworkManager.SendChat(new CSChat { channel = channel, content = content });

        // 自己的消息服务器不回送，本地补一条，保证「收发都要」
        chatLog.Add(new ChatRecord
        {
            channel = channel,
            kind = ChannelName(channel),
            camp = SelfCampName(),
            name = ClientSelection.playerName,
            content = content,
        });
        TrimChatLog();
        reply("已发送");
    }

    private void ReadChat(AiArgs a, Action<string> reply)
    {
        int channel = a.Int(0);
        if (channel < 0 || channel > SystemChannel)
        {
            reply("错误：channel 只能是 0 全部 / 1 阵营 / 2 附近 / 3 系统");
            return;
        }
        int count = a.Int(1);
        if (count <= 0)
        {
            reply("错误：数量必须是正整数");
            return;
        }

        var picked = new List<ChatRecord>();
        for (int i = 0; i < chatLog.Count; i++)
        {
            var record = chatLog[i];
            if (channel != 0 && record.channel != channel) continue;
            picked.Add(record);
        }
        if (picked.Count == 0)
        {
            reply("（无聊天记录）");
            return;
        }

        int start = Mathf.Max(0, picked.Count - count);
        var builder = new StringBuilder();
        for (int i = start; i < picked.Count; i++)
        {
            if (builder.Length > 0) builder.Append('\n');
            var r = picked[i];
            builder.Append('[').Append(r.kind).Append(']');
            if (r.channel != SystemChannel) builder.Append('[').Append(r.camp).Append(']').Append(r.name).Append(": ");
            builder.Append(r.content);
        }
        reply(builder.ToString());
    }

    private static string ChannelName(int channel)
    {
        switch (channel)
        {
            case 1: return "阵营";
            case 2: return "附近";
            default: return "全局";
        }
    }

    private void GetPlayerName(Action<string> reply)
    {
        reply(string.IsNullOrEmpty(ClientSelection.playerName) ? "名称=（空）" : "名称=" + ClientSelection.playerName);
    }

    private void ListCommands(Action<string> reply)
    {
        AiPhase phase = CurrentPhase;
        var builder = new StringBuilder();
        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if ((cmd.phase & phase) == 0) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(cmd.usage);
        }

        var lua = AiLuaInterface.Current;
        if (lua != null && lua.CommandNames.Count > 0)
        {
            builder.Append("\n--- lua 拓展（用 execLuaCommand 调用）---");
            for (int i = 0; i < lua.CommandNames.Count; i++)
            {
                builder.Append('\n').Append(lua.CommandNames[i]);
            }
        }
        reply(builder.ToString());
    }

    private void RunLuaExtension(AiArgs a, Action<string> reply)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            reply("错误：Lua 接口未就绪");
            return;
        }
        reply(lua.RunExtension(a.Str(0), a.Str(1)));
    }

    private void ReloadLua(Action<string> reply)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            reply("错误：Lua 接口未就绪");
            return;
        }
        reply(lua.ReloadExtensions());
    }
    #endregion

    #region Home / Lobby 命令
    private void SelectCharacter(AiArgs a, Action<string> reply)
    {
        int camp = a.Int(0);
        int index = a.Int(1);
        if (camp == 0)
        {
            if (index < 0 || index >= GlobalConfig.attack_character_count)
            {
                reply($"错误：进攻方下标范围 0~{GlobalConfig.attack_character_count - 1}");
                return;
            }
            ClientSelection.selectedAttackIndex = index;
        }
        else if (camp == 1)
        {
            if (index < 0 || index >= GlobalConfig.defense_character_count)
            {
                reply($"错误：防守方下标范围 0~{GlobalConfig.defense_character_count - 1}");
                return;
            }
            ClientSelection.selectedDefenseIndex = index;
        }
        else
        {
            reply("错误：camp 只能是 0 进攻方 / 1 防守方");
            return;
        }
        reply($"已选择 进攻={ClientSelection.selectedAttackIndex} 防守={ClientSelection.selectedDefenseIndex}");
    }

    private void Connect(AiArgs a, Action<string> reply)
    {
        if (connectTask != null)
        {
            reply("错误：上一次连接还没结束");
            return;
        }
        connectIp = a.Str(0);
        connectReply = reply;
        connectTask = Tool.NetworkManager.TryConnect(connectIp);
        reply("已发起连接：" + (connectIp.Length == 0 ? "本机" : connectIp));
    }

    private void TickConnect()
    {
        if (connectTask == null || !connectTask.IsCompleted) return;

        var task = connectTask;
        var reply = connectReply;
        connectTask = null;
        connectReply = null;

        if (task.IsFaulted)
        {
            string message = task.Exception != null ? task.Exception.GetBaseException().Message : "未知错误";
            Debug.LogError("[AI] 连接异常：" + message);
            reply("连接失败：" + message);
            return;
        }
        if (task.Result != NetworkManager.ConnectResult.Success)
        {
            reply("连接失败：" + task.Result);
            return;
        }

        Tool.NetworkManager.EnterWorld();
        reply("已连接，正在进入房间");
    }

    private void SetPlayerName(AiArgs a, Action<string> reply)
    {
        string name = a.Str(0);
        ClientSelection.playerName = name;
        reply("名称已设为 " + name);
    }

    // 控制模式只在 Home 可改：连接服务器时随 CSLobbyPlayerInfo 上报，进房后不允许再切
    private void Takeover(AiArgs a, Action<string> reply)
    {
        bool on = a.Bool(0);
        InputManager.SetControlMode(on ? PlayerControlMode.Ai : PlayerControlMode.Player);
        reply(on ? "已接管：键鼠与界面点击已屏蔽" : "已解除接管：恢复玩家控制");
    }

    // 阵营与准备同包上报，本地记住每次准备状态，改阵营时不能把准备悄悄清掉
    private void SelectCamp(AiArgs a, Action<string> reply)
    {
        int camp = a.Int(0);
        if (camp < 0 || camp > 2)
        {
            reply("错误：阵营只能是 0 进攻 / 1 防守 / 2 观战");
            return;
        }
        Tool.NetworkManager.SendLobbyUpdate(new CSLobbyUpdate { camp = camp, ready = lastReady });
        reply("已选择阵营 " + camp);
    }

    private void SetReady(AiArgs a, Action<string> reply)
    {
        lastReady = a.Bool(0);
        Tool.NetworkManager.SendLobbyUpdate(new CSLobbyUpdate { camp = -1, ready = lastReady });
        reply(lastReady ? "已准备" : "已取消准备");
    }

    private void LeaveRoom(Action<string> reply)
    {
        lastReady = false;
        Tool.NetworkManager.ExitWorld();
        reply("已离开房间");
    }
    #endregion

    #region Battle 命令
    private void MoveTo(AiArgs a, Action<string> reply) => SendIntent(AiIntent.MoveTo, new Vector2(a.Float(0), a.Float(1)), 0, reply);

    private void AttackCoral(Action<string> reply)
    {
        if (!TryFindNearestCoral(out ushort coralId))
        {
            reply("错误：小地图上没有珊瑚");
            return;
        }
        SendIntent(AiIntent.AttackTarget, Vector2.zero, coralId, reply);
    }

    private void CastSkill(AiArgs a, Action<string> reply)
    {
        int skillId = a.Int(0);
        if (!SkillManager.TryGet(skillId, out _))
        {
            reply($"错误：技能 {skillId} 未注册");
            return;
        }
        if (!NetworkManager.CanSendWorldCommand)
        {
            reply("错误：未联网");
            return;
        }
        Tool.NetworkManager.SendAiSkillCast(skillId);
        reply("已释放技能 " + skillId);
    }

    private void QuickMessage(AiArgs a, Action<string> reply)
    {
        int messageId = a.Int(0);
        if (!QuickMessageConfig.content.TryGetValue(messageId, out var cfg))
        {
            reply($"错误：快捷消息 {messageId} 未配置");
            return;
        }
        if (!NetworkManager.CanSendWorldCommand)
        {
            reply("错误：未联网");
            return;
        }
        Tool.NetworkManager.SendQuickMessage(new CSQuickMessage { messageId = messageId, hasTargetPosition = false });
        reply("已发送快捷消息 " + cfg.name);
    }

    private void QuickMessageWithPosition(AiArgs a, Action<string> reply)
    {
        int messageId = a.Int(0);
        if (!QuickMessageConfig.content.TryGetValue(messageId, out var cfg))
        {
            reply($"错误：快捷消息 {messageId} 未配置");
            return;
        }
        if (!NetworkManager.CanSendWorldCommand)
        {
            reply("错误：未联网");
            return;
        }

        float x = a.Float(1);
        float z = a.Float(2);
        Tool.NetworkManager.SendQuickMessage(new CSQuickMessage
        {
            messageId = messageId,
            hasTargetPosition = true,
            targetPosition = new Vector3(x, 0f, z),
        });
        reply($"已发送快捷消息 {cfg.name} 坐标=({x:0.##},{z:0.##})");
    }

    private void PathLength(AiArgs a, Action<string> reply)
    {
        float x = a.Float(0);
        float z = a.Float(1);
        if (!TryGetSelfPos(out Vector3 from))
        {
            reply("错误：本地玩家实体未就绪");
            return;
        }

        Vector3 to = new Vector3(x, from.y, z);
        if (!NavMesh.SamplePosition(from, out var startHit, 2f, NavMesh.AllAreas))
        {
            reply("可达=否 原因=起点不在导航面上");
            return;
        }
        if (!NavMesh.SamplePosition(to, out var endHit, 2f, NavMesh.AllAreas))
        {
            reply("可达=否 原因=终点不在导航面上");
            return;
        }

        var path = new NavMeshPath();
        bool ok = NavMesh.CalculatePath(startHit.position, endHit.position, NavMesh.AllAreas, path);
        if (!ok || path.corners.Length < 2)
        {
            reply("可达=否 原因=没有完整路径");
            return;
        }

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
        {
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }
        reply($"可达=是 长度={length:0.##} 拐点={path.corners.Length}");
    }

    private void ActivateLua(Action<string> reply)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            reply("错误：Lua 接口未就绪");
            return;
        }
        reply(lua.ActivateStateMachine());
    }

    private void StopLua(Action<string> reply)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            reply("错误：Lua 接口未就绪");
            return;
        }
        reply(lua.StopStateMachine());
    }

    private void MinimapEntities(Action<string> reply)
    {
        if (minimap.Count == 0)
        {
            reply("（小地图上没有实体）");
            return;
        }

        var ids = new List<ushort>(minimap.Keys);
        ids.Sort();
        var builder = new StringBuilder();
        for (int i = 0; i < ids.Count; i++)
        {
            var e = minimap[ids[i]];
            if (builder.Length > 0) builder.Append('\n');
            builder.Append("id=").Append(e.entityId)
                .Append(" 类型=").Append(e.type.category).Append(':').Append(e.type.value)
                .Append(" 阵营=").Append(e.camp)
                .Append(" 坐标=(").Append(e.posX.ToString("0.##")).Append(',').Append(e.posZ.ToString("0.##")).Append(')')
                .Append(" 标记=").Append(e.marked ? 1 : 0);
        }
        reply(builder.ToString());
    }

    private void SkillList(Action<string> reply)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null)
        {
            reply("错误：技能管理器未就绪");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("槽位=").Append(skills.SlotCapacity);
        var active = skills.ActiveSkills;
        for (int i = 0; i < active.Count; i++)
        {
            int skillId = active[i];
            builder.Append('\n').Append("槽位").Append(i)
                .Append(": id=").Append(skillId)
                .Append(" 进度=").Append(skills.Recharge.GetCycleProgress(skillId).ToString("0.##"))
                .Append(" 库存=").Append(skills.Recharge.GetStore(skillId));
        }

        builder.Append("\n槽位外持有:");
        bool any = false;
        var held = skills.HeldSkills;
        for (int i = 0; i < held.Count; i++)
        {
            if (ContainsSkill(active, held[i])) continue;
            builder.Append(' ').Append(held[i]);
            any = true;
        }
        if (!any) builder.Append(" 无");
        reply(builder.ToString());
    }

    private static bool ContainsSkill(IReadOnlyList<int> ids, int skillId)
    {
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] == skillId) return true;
        }
        return false;
    }

    private void SetSkillBar(AiArgs a, Action<string> reply)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null)
        {
            reply("错误：技能管理器未就绪");
            return;
        }

        var ids = a.IntList(0);
        for (int i = 0; i < ids.Count; i++)
        {
            if (ids[i] < 0)
            {
                reply("错误：技能 id 必须是非负整数");
                return;
            }
            if (ids.IndexOf(ids[i]) != i)
            {
                reply("错误：技能 id 重复 " + ids[i]);
                return;
            }
        }

        if (!skills.TrySetActiveSkills(ids))
        {
            reply("错误：技能栏提交被拒（未持有或超过槽位数）");
            return;
        }
        reply("已提交技能栏，等服务器回推快照");
    }

    private void CurrentPos(Action<string> reply)
    {
        if (!TryGetSelfPos(out Vector3 pos))
        {
            reply("错误：本地玩家实体未就绪");
            return;
        }
        reply($"坐标=({pos.x:0.##},{pos.z:0.##})");
    }

    private void SelfStatus(Action<string> reply)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession)
        {
            reply("错误：没有战斗会话");
            return;
        }
        if (!TryGetSelfPos(out Vector3 pos))
        {
            reply("错误：本地玩家实体未就绪");
            return;
        }

        var builder = new StringBuilder();
        builder.Append("实体=").Append(loading.OwnEntityId)
            .Append(" 阵营=").Append(loading.Camp)
            .Append(" 血量=").Append(selfHealth).Append('/').Append(selfMaxHealth)
            .Append(" 坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(SelfYaw().ToString("0.#"));

        builder.Append(" buff=");
        if (selfBuffs.Count == 0) builder.Append("无");
        for (int i = 0; i < selfBuffs.Count; i++)
        {
            if (i > 0) builder.Append(',');
            builder.Append(selfBuffs[i]);
        }
        reply(builder.ToString());
    }

    // 自身 + 守护点 + 阵营人数 + 昼夜 + 小地图条数，一次取全现场
    private void GetInfo(Action<string> reply)
    {
        var builder = new StringBuilder();

        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession)
        {
            reply("错误：没有战斗会话");
            return;
        }
        if (!TryGetSelfPos(out Vector3 pos))
        {
            reply("错误：本地玩家实体未就绪");
            return;
        }

        builder.Append("自身 实体=").Append(loading.OwnEntityId)
            .Append(" 阵营=").Append(loading.Camp)
            .Append(" 血量=").Append(selfHealth).Append('/').Append(selfMaxHealth)
            .Append(" 坐标=(").Append(pos.x.ToString("0.##")).Append(',').Append(pos.z.ToString("0.##")).Append(')')
            .Append(" 朝向=").Append(SelfYaw().ToString("0.#"));

        builder.Append("\n守护点 ");
        if (guardProgress == null) builder.Append("未收到");
        else
        {
            builder.Append("主=").Append(guardProgress.mainHealth)
                .Append(" 副1=").Append(guardProgress.sub1Health)
                .Append(" 副2=").Append(guardProgress.sub2Health)
                .Append(" 副3=").Append(guardProgress.sub3Health);
        }

        builder.Append("\n阵营 ");
        var camp = NetworkManager.LatestCampAliveCount;
        if (camp == null) builder.Append("未收到");
        else
        {
            builder.Append("进攻=").Append(camp.attackerAlive).Append('/').Append(camp.attackerTotal)
                .Append(" 防守=").Append(camp.defenseAlive).Append('/').Append(camp.defenseTotal)
                .Append(" 观战=").Append(camp.spectatorCount);
        }

        builder.Append("\n昼夜 ").Append(BattleManager.IsDay ? "白天" : "夜晚")
            .Append(" 周期时间=").Append(BattleManager.CycleTime.ToString("0.##"));

        builder.Append("\n小地图 ").Append(minimap.Count);

        reply(builder.ToString());
    }

    private void GuardProgress(Action<string> reply)
    {
        if (guardProgress == null)
        {
            reply("错误：尚未收到守护点数据");
            return;
        }
        reply($"主={guardProgress.mainHealth} 副1={guardProgress.sub1Health} 副2={guardProgress.sub2Health} 副3={guardProgress.sub3Health}（当前血量，上限 = entity.csv 生命 × 人数倍率）");
    }

    private void DayNightInfo(Action<string> reply)
    {
        reply($"昼夜={(BattleManager.IsDay ? "白天" : "夜晚")} 周期时间={BattleManager.CycleTime:0.##}（0~2，0/2 午夜、1 正午）");
    }

    private void CampCount(Action<string> reply)
    {
        var camp = NetworkManager.LatestCampAliveCount;
        if (camp == null)
        {
            reply("错误：尚未收到阵营人数");
            return;
        }
        reply($"进攻={camp.attackerAlive}/{camp.attackerTotal} 防守={camp.defenseAlive}/{camp.defenseTotal} 观战={camp.spectatorCount}");
    }

    private void PointInfo(Action<string> reply)
    {
        if (!TerrainSpawns.Built)
        {
            reply("错误：地形点位未就绪");
            return;
        }

        var builder = new StringBuilder();
        AppendPoint(builder, "主守护点", TerrainSpawns.MainGuard);
        for (int i = 0; i < TerrainSpawns.SubGuards.Count; i++)
            AppendPoint(builder, "副守护点" + (i + 1), TerrainSpawns.SubGuards[i]);
        for (int i = 0; i < TerrainSpawns.Towers.Count; i++)
            AppendPoint(builder, "防御塔" + (i + 1), TerrainSpawns.Towers[i]);
        for (int i = 0; i < TerrainSpawns.NeutralStatues.Count; i++)
            AppendPoint(builder, "中立雕像" + (i + 1), TerrainSpawns.NeutralStatues[i]);
        AppendPoint(builder, "进攻出生", TerrainSpawns.AttackSpawn);
        AppendPoint(builder, "防守出生", TerrainSpawns.DefenseSpawn);
        reply(builder.ToString());
    }

    private static void AppendPoint(StringBuilder builder, string label, Vector3 point)
    {
        if (builder.Length > 0) builder.Append('\n');
        builder.Append(label).Append("=(").Append(point.x.ToString("0.##")).Append(',').Append(point.z.ToString("0.##")).Append(')');
    }

    private void SetCameraRender(AiArgs a, Action<string> reply)
    {
        bool on = a.Bool(0);
        var cam = Tool.CameraController != null ? Tool.CameraController.WorldCamera : null;
        if (cam == null)
        {
            reply("错误：相机未就绪");
            return;
        }
        cam.enabled = on;
        reply(on ? "相机渲染已开启" : "相机渲染已关闭（截屏会自动开启一帧）");
    }

    private void Screenshot(Action<string> reply)
    {
        var cam = Tool.CameraController != null ? Tool.CameraController.WorldCamera : null;
        if (cam == null)
        {
            reply("错误：相机未就绪");
            return;
        }
        if (!TryGetSelfPos(out Vector3 pos))
        {
            reply("错误：本地玩家实体未就绪");
            return;
        }

        string dir = Path.Combine(Application.dataPath, "..", ScreenshotFolder);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "ai_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");

        bool restore = !cam.enabled;
        if (restore) cam.enabled = true;
        ScreenCapture.CaptureScreenshot(file);
        if (restore) Timer.AddTimer(this, _ => RestoreCamera(cam), 0.2f, 1, false);

        reply($"文件={Path.GetFullPath(file)} 坐标=({pos.x:0.##},{pos.z:0.##}) 朝向={SelfYaw():0.#}");
    }

    private static void RestoreCamera(Camera cam)
    {
        if (cam != null) cam.enabled = false;
    }
    #endregion

    #region 意图
    public static bool TrySendIntent(AiIntent intent, Vector2 position, ushort targetId, out string error)
    {
        if (IntentSender == null)
        {
            error = "意图通道未接入（等待意图上行协议）";
            return false;
        }
        return IntentSender(intent, position, targetId, out error);
    }

    private void SendIntent(AiIntent intent, Vector2 position, ushort targetId, Action<string> reply)
    {
        if (!TrySendIntent(intent, position, targetId, out string error))
        {
            reply("错误：" + error);
            return;
        }
        reply("已下发 " + intent);
    }
    #endregion

    #region 事件
    private void OnChatMessage(SCChat chat)
    {
        chatLog.Add(new ChatRecord
        {
            channel = chat.channel,
            kind = ChannelName(chat.channel),
            camp = ((EntityCamp)chat.camp).ToString(),
            name = chat.name,
            content = chat.content,
        });
        TrimChatLog();

        string self = ClientSelection.playerName;
        if (string.IsNullOrEmpty(self)) return;
        if (chat.content == null || !chat.content.Contains("@" + self)) return;
        hub.Push(AiInterruptKind.ChatMention, "发送者=" + chat.name, "频道=" + chat.channel, "内容=" + chat.content);
    }

    private void OnBattleEvent(SCBattleEvent e)
    {
        if (e == null) return;
        string text = ResolveEventText(e);
        if (text == null) return;
        chatLog.Add(new ChatRecord { channel = SystemChannel, kind = "系统", content = text });
        TrimChatLog();
    }

    private void OnPrompt(int messageId)
    {
        chatLog.Add(new ChatRecord
        {
            channel = SystemChannel,
            kind = "系统",
            content = ResolvePrompt(messageId),
        });
        TrimChatLog();
    }

    private void OnMinimapUpdate(SCMinimapEntity e)
    {
        if (e == null) return;
        minimap[e.entityId] = e;
        minimapSeen[e.entityId] = Time.time;
    }

    private void OnEntityDisplay(SCEntityDisplayInfo info)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || info.entityId != loading.OwnEntityId) return;

        if (info.hasHealth)
        {
            selfHealth = info.health;
            selfMaxHealth = info.maxHealth;
        }
        if (!info.hasBuffs) return;

        selfBuffs.Clear();
        for (int i = 0; i < info.buffs.Count; i++)
        {
            selfBuffs.Add(info.buffs[i].type + ":" + info.buffs[i].level);
        }
    }

    private void OnGuardProgress(SCDefensePointProgress info) => guardProgress = info;

    private void OnBattleStart(int camp) => hub.Push(AiInterruptKind.BattleStart, "阵营=" + (EntityCamp)camp);

    private void OnBattleEnd(int gameState) => hub.Push(AiInterruptKind.BattleEnd, "结算=" + gameState);
    #endregion

    #region 工具
    private void TickMinimapExpire()
    {
        if (minimapSeen.Count == 0) return;

        minimapExpired.Clear();
        foreach (var pair in minimapSeen)
        {
            if (Time.time - pair.Value <= GlobalConfig.minimap_entity_timeout) continue;
            minimapExpired.Add(pair.Key);
        }
        for (int i = 0; i < minimapExpired.Count; i++)
        {
            ushort id = minimapExpired[i];
            minimapSeen.Remove(id);
            minimap.Remove(id);
        }
    }

    private static List<string> SplitArgs(string line)
    {
        var tokens = new List<string>();
        var builder = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                quoted = !quoted;
                continue;
            }
            if (!quoted && (c == ' ' || c == '\t'))
            {
                if (builder.Length > 0)
                {
                    tokens.Add(builder.ToString());
                    builder.Clear();
                }
                continue;
            }
            builder.Append(c);
        }
        if (builder.Length > 0) tokens.Add(builder.ToString());
        if (tokens.Count == 0) tokens.Add(string.Empty);
        return tokens;
    }

    private void TrimChatLog()
    {
        while (chatLog.Count > ChatLogCapacity) chatLog.RemoveAt(0);
    }

    private static string SelfCampName()
    {
        var loading = ClientLoadingManager.Current;
        return loading == null ? "未定" : loading.Camp.ToString();
    }

    public bool TryGetSelfPos(out Vector3 pos)
    {
        pos = default;
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession) return false;
        if (Tool.ClientLogicManager == null) return false;
        return Tool.ClientLogicManager.EntityPlayers.TryGetEntityPosition(loading.OwnEntityId, out pos);
    }

    public float SelfYaw()
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession) return 0f;
        if (Tool.ClientLogicManager == null) return 0f;
        var view = Tool.ClientLogicManager.EntityPlayers.GetView(loading.OwnEntityId);
        if (view == null) return 0f;
        return view.transform.eulerAngles.y;
    }

    private bool TryFindNearestCoral(out ushort coralId)
    {
        coralId = 0;
        if (!TryGetSelfPos(out Vector3 self)) return false;

        float best = float.MaxValue;
        bool found = false;
        foreach (var pair in minimap)
        {
            var e = pair.Value;
            if (e.type.category != EntityCategory.Coral) continue;
            float dx = e.posX - self.x;
            float dz = e.posZ - self.z;
            float sqr = dx * dx + dz * dz;
            if (sqr >= best) continue;
            best = sqr;
            coralId = e.entityId;
            found = true;
        }
        return found;
    }

    private static string ResolveEventText(SCBattleEvent e)
    {
        switch (e.type)
        {
            case SCBattleEvent.Type.PlayerKill:
                return ResolveText(e.textId) + " 击败 " + ResolveText(e.textId2);
            case SCBattleEvent.Type.DeathUnattributed:
                return ResolveText(e.textId) + " 死亡";
            case SCBattleEvent.Type.NeutralStatueCaptured:
                return "中立雕像被击败";
            case SCBattleEvent.Type.NeutralStatueRespawn:
                return "中立雕像刷新";
            case SCBattleEvent.Type.Nightfall:
                return "天黑";
            case SCBattleEvent.Type.Daybreak:
                return "天亮";
            case SCBattleEvent.Type.StatueDestroyed:
                return "雕像被摧毁";
            case SCBattleEvent.Type.Notice:
                return ResolveText(e.textId);
            default:
                return null;
        }
    }

    // 事件文本 id：≥10000 是玩家名，<10000 按 system_message 枚举查表
    private static string ResolveText(int id)
    {
        if (id >= SCBattleEvent.PlayerNameBase)
        {
            int clientId = id - SCBattleEvent.PlayerNameBase;
            var loading = ClientLoadingManager.Current;
            string name = loading != null ? loading.GetName(clientId) : null;
            return string.IsNullOrEmpty(name) ? "#" + clientId : name;
        }
        var key = (system_messageEnum)id;
        if (!SystemMessageConfig.content.ContainsKey(key))
        {
            Debug.LogError($"[AI] system_message 缺少枚举 id={id}");
            return "#" + id;
        }
        return SystemMessageConfig.Get(key).text;
    }

    // 飘字提示 id 按 prompt 枚举查表
    private static string ResolvePrompt(int messageId)
    {
        var key = (promptEnum)messageId;
        if (!PromptConfig.content.ContainsKey(key))
        {
            Debug.LogError($"[AI] prompt 缺少枚举 id={messageId}");
            return "#" + messageId;
        }
        return PromptConfig.Get(key).text;
    }
    #endregion
}
