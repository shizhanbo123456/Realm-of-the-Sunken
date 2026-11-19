using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ros.Config;
using Ros.Transport;
using UnityEngine;

public enum AiInterruptKind
{
    CommandReply,
    CommandDone,
    LuaTrigger,
    WaitTimeout,
    LuaError,
    BattleStart,
    BattleEnd,
    MoveFailed,
}

// 中断信息缓冲：命令回复 / 命令已执行 / Lua 触发 / 等待时间到 都进这里，取走时整批清空
public sealed class AiInterruptHub
{
    private const int Capacity = 64;
    private const string OriginCmd = "cmd";
    private const string OriginLua = "lua";

    private struct Entry
    {
        public AiInterruptKind kind;
        public string[] fields;
    }

    private readonly List<Entry> entries = new();
    private readonly StringBuilder builder = new();
    private string origin = OriginCmd;
    private string waitOrigin = OriginCmd;
    private float waitSeconds;
    private float waitDeadline; // <= 0 表示当前没在等

    // 只影响日志前缀：Lua 期间产生的中断记作 [lua输出]
    public void SetOriginLua() => origin = OriginLua;

    public void SetOriginCmd() => origin = OriginCmd;

    public void Push(AiInterruptKind kind, params string[] fields) => PushWith(origin, kind, fields);

    public void PushLua(AiInterruptKind kind, params string[] fields) => PushWith(OriginLua, kind, fields);

    private void PushWith(string from, AiInterruptKind kind, params string[] fields)
    {
        var entry = new Entry { kind = kind, fields = fields };
        entries.Add(entry);
        while (entries.Count > Capacity) entries.RemoveAt(0);
#if UNITY_EDITOR
        Log(entry, from);
#endif
    }

    private void Log(Entry entry, string from)
    {
        builder.Clear();
        Append(builder, entry);
        string text = builder.ToString();
        if (entry.kind == AiInterruptKind.LuaError || from == OriginLua) AiCommandLog.LuaOut(text);
        else AiCommandLog.CmdOut(text);
    }

    // 等待一定时间后自动触发一次中断：只挂倒计时，命令本身立即返回
    public void Wait(float seconds)
    {
        waitSeconds = seconds;
        waitDeadline = Time.time + seconds;
        waitOrigin = origin;
    }

    public void Tick()
    {
        if (waitDeadline <= 0f || Time.time < waitDeadline) return;
        waitDeadline = 0f;
        PushWith(waitOrigin, AiInterruptKind.WaitTimeout, "等待=" + waitSeconds.ToString("0.##"));
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
            Append(builder, entries[i]);
        }
        return builder.ToString();
    }

    private static void Append(StringBuilder text, Entry e)
    {
        text.Append('[').Append(Label(e.kind)).Append(']');
        for (int j = 0; j < e.fields.Length; j++)
        {
            text.Append(j == 0 ? " " : " | ").Append(e.fields[j]);
        }
    }

    private static string Label(AiInterruptKind kind)
    {
        switch (kind)
        {
            case AiInterruptKind.CommandReply: return "命令回复";
            case AiInterruptKind.CommandDone: return "命令已执行";
            case AiInterruptKind.LuaTrigger: return "Lua触发";
            case AiInterruptKind.WaitTimeout: return "等待时间到";
            case AiInterruptKind.LuaError: return "Lua错误";
            case AiInterruptKind.BattleStart: return "战斗开始";
            case AiInterruptKind.BattleEnd: return "战斗结束";
            case AiInterruptKind.MoveFailed: return "移动失败";
            default: return kind.ToString();
        }
    }
}

// AI 操控的命令行入口：命令表来自 ai_command 配表，解析后按主键走事件总线，谁注册谁处理
public sealed class AiCommandLine : ClientSubManager
{
    public static AiCommandLine Current { get; private set; }

    private sealed class Command
    {
        public ai_commandEnum id;
        public string name;
        public string usage;
        public ai_command row;
        public bool noReply;   // 表里标了「无返回值」：执行完直接推一条「命令已执行」
        public bool silent;    // 命令自己会产出中断（interrupt / waitForIntrrupt），不再补「命令已执行」
    }

    private struct ParamDef
    {
        public string name;
        public string type;
    }

    private const int SystemChannel = 3;
    private const int MaxReadCount = 200;
    private const string NoReplyMark = "无返回值";

    private readonly List<Command> commands = new();
    private readonly Dictionary<string, Command> byName = new();
    private readonly AiObservationLog observations = new();
    private readonly AiInterruptHub hub = new();
    private bool registerWarned;

    public AiInterruptHub Interrupts => hub;

    // 三条缓冲的读取口：命令行命令与 Lua 的 Tick 状态机共用
    public int ChatTotal => observations.ChatTotal;

    public int PromptTotal => observations.PromptTotal;

    public string ReadChat(int channel, int count) => observations.ReadChat(channel, count);

    public string ReadPrompt(int count) => observations.ReadPrompt(count);

    public string ReadImportant(int count) => observations.ReadImportant(count);

    public override void Init(ClientLogicManager owner)
    {
        base.Init(owner);
        Current = this;
        RegisterFromConfig();
        BindSelfCommands();
    }

    public override void Tick(float deltaTime) => hub.Tick();

    #region 对外入口
    // 外部命令行：结果一律转成中断，调用方等中断而不是等返回值
    public void Submit(string line)
    {
        AiCommandLog.CmdIn(line ?? "");
        if (string.IsNullOrEmpty(line) || line.Trim().Length == 0)
        {
            PushReply("", "错误：空命令");
            return;
        }
        if (commands.Count == 0) RegisterFromConfig();

        var tokens = SplitArgs(line);
        var cmd = Find(tokens[0]);
        if (cmd == null)
        {
            PushReply(tokens[0], $"错误：未知命令 {tokens[0]}（用 listCommands 查看）");
            return;
        }

        if (!AiCommandBus.Ready(cmd.id))
        {
            PushReply(cmd.name, $"错误：命令 {cmd.name} 在当前阶段不可用");
            return;
        }

        if (!TryParse(cmd, tokens, out object[] parsed, out string error))
        {
            PushReply(cmd.name, error);
            return;
        }

        bool replied = false;
        AiCommandBus.Call(cmd.id, new AiRequest(cmd.name, parsed, text =>
        {
            replied = true;
            PushReply(cmd.name, text);
        }));

        if (replied || cmd.silent) return;
        if (cmd.noReply) hub.Push(AiInterruptKind.CommandDone, cmd.name);
    }

    // Lua 内部调用：同一条解析与分发链路，回复直接当返回值，不进中断
    public string Execute(string line)
    {
        AiCommandLog.LuaIn(line ?? "");
        hub.SetOriginLua();
        string reply;
        try
        {
            reply = ExecuteInternal(line);
        }
        finally
        {
            hub.SetOriginCmd();
        }
        if (!string.IsNullOrEmpty(reply)) AiCommandLog.LuaOut(reply);
        return reply;
    }

    private string ExecuteInternal(string line)
    {
        if (string.IsNullOrEmpty(line) || line.Trim().Length == 0) return "错误：空命令";
        if (commands.Count == 0) RegisterFromConfig();

        var tokens = SplitArgs(line);
        var cmd = Find(tokens[0]);
        if (cmd == null) return $"错误：未知命令 {tokens[0]}";
        if (!AiCommandBus.Ready(cmd.id)) return $"错误：命令 {cmd.name} 在当前阶段不可用";
        if (!TryParse(cmd, tokens, out object[] parsed, out string error)) return error;

        string reply = null;
        AiCommandBus.Call(cmd.id, new AiRequest(cmd.name, parsed, text => reply = text));
        return reply;
    }

    // Lua 侧问「这条命令现在能不能用」，口径与命令行一致
    public bool Can(string command)
    {
        var cmd = Find(command);
        return cmd != null && AiCommandBus.Ready(cmd.id);
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
                name = row.key.ToString(),
                usage = Usage(row),
                row = row,
                noReply = row.commandHelp != null && row.commandHelp.Contains(NoReplyMark),
                silent = row.key == ai_commandEnum.interrupt || row.key == ai_commandEnum.waitForIntrrupt,
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

    // 常驻命令：不依赖任何界面阶段，AI 自己持有（缓冲区与中断都在这里）
    private void BindSelfCommands()
    {
        AiCommandBus.Bind(ai_commandEnum.listCommands, OnListCommands);
        AiCommandBus.Bind(ai_commandEnum.checkInterrupt, OnCheckInterrupt);
        AiCommandBus.Bind(ai_commandEnum.interrupt, OnInterrupt);
        AiCommandBus.Bind(ai_commandEnum.waitForIntrrupt, OnWait);
        AiCommandBus.Bind(ai_commandEnum.chat_recent, OnChatRecent);
        AiCommandBus.Bind(ai_commandEnum.prompt_buffer, OnPromptBuffer);
        AiCommandBus.Bind(ai_commandEnum.lua_reloadExtension, OnReloadLua);
        AiCommandBus.Bind(ai_commandEnum.lua_execCommand, OnRunLuaExtension);
        AiCommandBus.Bind(ai_commandEnum.home_getPlayerName, OnGetPlayerName);
    }

    private void UnbindSelfCommands()
    {
        AiCommandBus.Unbind(ai_commandEnum.listCommands, OnListCommands);
        AiCommandBus.Unbind(ai_commandEnum.checkInterrupt, OnCheckInterrupt);
        AiCommandBus.Unbind(ai_commandEnum.interrupt, OnInterrupt);
        AiCommandBus.Unbind(ai_commandEnum.waitForIntrrupt, OnWait);
        AiCommandBus.Unbind(ai_commandEnum.chat_recent, OnChatRecent);
        AiCommandBus.Unbind(ai_commandEnum.prompt_buffer, OnPromptBuffer);
        AiCommandBus.Unbind(ai_commandEnum.lua_reloadExtension, OnReloadLua);
        AiCommandBus.Unbind(ai_commandEnum.lua_execCommand, OnRunLuaExtension);
        AiCommandBus.Unbind(ai_commandEnum.home_getPlayerName, OnGetPlayerName);
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
        var builder = new StringBuilder(row.key.ToString());
        var parameters = Params(row);
        for (int i = 0; i < parameters.Count; i++)
        {
            builder.Append(' ').Append(parameters[i].name).Append(':').Append(parameters[i].type);
        }
        return builder.ToString();
    }
    #endregion

    #region 参数
    private static bool TryParse(Command cmd, List<string> tokens, out object[] args, out string error)
    {
        var rest = new string[tokens.Count - 1];
        for (int i = 0; i < rest.Length; i++) rest[i] = tokens[i + 1];
        return TryBuildArgs(cmd.row, rest, out args, out error);
    }

    // 表里的最后一个 string（非首位）与 List<int> 吃掉剩余 token：聊天内容、lua 参数、技能列表都这样传
    private static bool TryBuildArgs(ai_command row, string[] tokens, out object[] args, out string error)
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

        args = values;
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

    #region 常驻命令
    private void PushReply(string command, string text)
    {
        hub.Push(AiInterruptKind.CommandReply, string.IsNullOrEmpty(command) ? "-" : command, text);
    }

    private void OnGetPlayerName(AiRequest request)
    {
        request.Reply(string.IsNullOrEmpty(ClientSelection.playerName) ? "名称=（空）" : "名称=" + ClientSelection.playerName);
    }

    private void OnChatRecent(AiRequest request)
    {
        int channel = request.Int(0);
        if (channel < 0 || channel > SystemChannel)
        {
            request.Reject("channel 只能是 0 全部 / 1 阵营 / 2 附近 / 3 系统");
            return;
        }
        int count = Count(request.Int(1));
        if (count <= 0)
        {
            request.Reject($"数量必须是 1~{MaxReadCount} 的整数");
            return;
        }
        request.Reply(observations.ReadChat(channel, count) ?? "（没有聊天记录）");
    }

    private void OnPromptBuffer(AiRequest request)
    {
        int count = Count(request.Int(0));
        if (count <= 0)
        {
            request.Reject($"数量必须是 1~{MaxReadCount} 的整数");
            return;
        }
        request.Reply(observations.ReadPrompt(count) ?? "（没有提示记录）");
    }

    private void OnCheckInterrupt(AiRequest request)
    {
        request.Reply(hub.TryDrain(out string text) ? text : "（没有中断）");
    }

    private void OnInterrupt(AiRequest request)
    {
        string message = request.Str(0);
        hub.Push(AiInterruptKind.LuaTrigger, string.IsNullOrEmpty(message) ? "lua 触发" : message);
    }

    private void OnWait(AiRequest request)
    {
        float seconds = request.Float(0);
        if (seconds <= 0f)
        {
            request.Reject("等待时间必须大于 0");
            return;
        }
        hub.Wait(seconds);
    }

    private void OnListCommands(AiRequest request)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < commands.Count; i++)
        {
            var cmd = commands[i];
            if (!AiCommandBus.Ready(cmd.id)) continue;
            if (builder.Length > 0) builder.Append('\n');
            builder.Append(cmd.usage);
        }

        var lua = AiLuaInterface.Current;
        if (lua != null && lua.CommandNames.Count > 0)
        {
            builder.Append("\n--- lua 拓展（用 lua_execCommand 调用）---");
            for (int i = 0; i < lua.CommandNames.Count; i++)
            {
                builder.Append('\n').Append(lua.CommandNames[i]);
            }
        }
        request.Reply(builder.ToString());
    }

    private void OnRunLuaExtension(AiRequest request)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            request.Reject("Lua 接口未就绪");
            return;
        }
        request.Reply(lua.RunExtension(request.Str(0), request.Str(1)));
    }

    private void OnReloadLua(AiRequest request)
    {
        var lua = AiLuaInterface.Current;
        if (lua == null)
        {
            request.Reject("Lua 接口未就绪");
            return;
        }
        request.Reply(lua.ReloadExtensions());
    }

    private static int Count(int count) => count <= 0 || count > MaxReadCount ? -1 : count;
    #endregion

    #region 事件
    protected override void BindEvents()
    {
        EventManager.AddEvent<AiChatLine>(AiEvent.ChatLine, OnChatLine);
        EventManager.AddEvent<AiTextLine>(AiEvent.PromptLine, OnPromptLine);
        EventManager.AddEvent<AiTextLine>(AiEvent.ImportantLine, OnImportantLine);
        EventManager.AddEvent<SCAiFeedback>(ClientEvent.OnAiFeedback, OnServerFeedback);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.AddEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
    }

    protected override void UnbindEvents()
    {
        EventManager.RemoveEvent<AiChatLine>(AiEvent.ChatLine, OnChatLine);
        EventManager.RemoveEvent<AiTextLine>(AiEvent.PromptLine, OnPromptLine);
        EventManager.RemoveEvent<AiTextLine>(AiEvent.ImportantLine, OnImportantLine);
        EventManager.RemoveEvent<SCAiFeedback>(ClientEvent.OnAiFeedback, OnServerFeedback);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        UnbindSelfCommands();
        if (Current == this) Current = null;
    }

    private void OnChatLine(AiChatLine line) => observations.AppendChat(line);

    private void OnPromptLine(AiTextLine line) => observations.AppendPrompt(line);

    private void OnImportantLine(AiTextLine line) => observations.AppendImportant(line);

    // 服务器回执：目前只有移动失败（目的地算不出路）
    private void OnServerFeedback(SCAiFeedback info)
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

    private void OnBattleStart(int camp) => hub.Push(AiInterruptKind.BattleStart, "阵营=" + (EntityCamp)camp);

    private void OnBattleEnd(int gameState) => hub.Push(AiInterruptKind.BattleEnd, "结算=" + gameState);
    #endregion

    #region 工具
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
    #endregion
}

// 编辑器下把 AI 命令的收发打到控制台，打包后为空实现
internal static class AiCommandLog
{
    public static void CmdIn(string line)
    {
#if UNITY_EDITOR
        Debug.Log("[cmd输入] " + line);
#endif
    }

    public static void CmdOut(string text)
    {
#if UNITY_EDITOR
        Debug.Log("[cmd输出] " + text);
#endif
    }

    public static void LuaIn(string line)
    {
#if UNITY_EDITOR
        Debug.Log("[lua输入] " + line);
#endif
    }

    // Lua 的 typed API 没有文本行，按「命令 + 参数」拼一行
    public static void LuaCall(string command, object[] values)
    {
#if UNITY_EDITOR
        Debug.Log("[lua输入] " + (values.Length == 0 ? command : command + " " + string.Join(" ", values)));
#endif
    }

    public static void LuaOut(string text)
    {
#if UNITY_EDITOR
        Debug.Log("[lua输出] " + text);
#endif
    }
}
