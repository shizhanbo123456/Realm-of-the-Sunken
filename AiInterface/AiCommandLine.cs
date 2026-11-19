using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Ros.Config;
using Ros.Transport;
using UnityEngine;

public enum AiInterruptKind
{
    ChatMention,
    LuaTrigger,
    LuaError,
    BattleStart,
    BattleEnd,
    MoveFailed,
}

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

// AI 操控的命令行入口：命令表来自 ai_command 配表，解析后按主键走事件总线，谁注册谁处理
public sealed class AiCommandLine : ClientSubManager
{
    public static AiCommandLine Current { get; private set; }

    private sealed class Command
    {
        public int id;
        public string name;
        public string usage;
        public ai_command row;
    }

    private struct ParamDef
    {
        public string name;
        public string type;
    }

    private const int SystemChannel = 3;
    private const int MaxReadCount = 200;

    private readonly List<Command> commands = new();
    private readonly Dictionary<string, Command> byName = new();
    private readonly AiObservationLog observations = new();
    private readonly AiInterruptHub hub = new();
    private bool registerWarned;

    public AiInterruptHub Interrupts => hub;

    public override void Init(ClientLogicManager owner)
    {
        base.Init(owner);
        Current = this;
        RegisterFromConfig();
        BindSelfCommands();
    }

    #region 对外入口
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

        if (!AiCommandBus.Ready(cmd.id))
        {
            onResult($"错误：命令 {cmd.name} 在当前阶段不可用");
            return;
        }

        var args = new string[tokens.Count - 1];
        for (int i = 0; i < args.Length; i++) args[i] = tokens[i + 1];

        if (!TryBuildArgs(cmd.row, args, out object[] parsed, out string error))
        {
            onResult(error);
            return;
        }
        AiCommandBus.Call(cmd.id, new AiRequest(cmd.name, parsed, onResult));
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
                name = row.command,
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

    // 常驻命令：不依赖任何界面阶段，AI 自己持有
    private void BindSelfCommands()
    {
        AiCommandBus.Bind(AiCommandId.GetMyPlayerName, OnGetPlayerName);
        AiCommandBus.Bind(AiCommandId.ReadChatMessage, OnReadChat);
        AiCommandBus.Bind(AiCommandId.ReadEvents, OnReadEvents);
        AiCommandBus.Bind(AiCommandId.ReloadLuaCommand, OnReloadLua);
        AiCommandBus.Bind(AiCommandId.ExecLuaCommand, OnRunLuaExtension);
        AiCommandBus.Bind(AiCommandId.ListCommands, OnListCommands);
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
    private void OnGetPlayerName(AiRequest request)
    {
        request.Reply(string.IsNullOrEmpty(ClientSelection.playerName) ? "名称=（空）" : "名称=" + ClientSelection.playerName);
    }

    private void OnReadChat(AiRequest request)
    {
        int channel = request.Int(0);
        if (channel < 0 || channel > SystemChannel)
        {
            request.Reject("channel 只能是 0 全部 / 1 阵营 / 2 附近 / 3 系统");
            return;
        }
        request.Reply(ReadObservations(channel, request.Int(1), "聊天"));
    }

    private void OnReadEvents(AiRequest request)
    {
        request.Reply(ReadObservations(-1, request.Int(0), "事件"));
    }

    private string ReadObservations(int channel, int count, string label)
    {
        if (count <= 0 || count > MaxReadCount)
        {
            return $"错误：数量必须是 1~{MaxReadCount} 的整数";
        }
        string text = channel < 0 ? observations.Read(count) : observations.ReadChat(channel, count);
        return text ?? $"（没有{label}记录）";
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
            builder.Append("\n--- lua 拓展（用 execLuaCommand 调用）---");
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
    #endregion

    #region 事件
    protected override void BindEvents()
    {
        EventManager.AddEvent<AiObservation>(AiEvent.Observation, OnObservation);
        EventManager.AddEvent<SCAiFeedback>(ClientEvent.OnAiFeedback, OnServerFeedback);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.AddEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
    }

    protected override void UnbindEvents()
    {
        EventManager.RemoveEvent<AiObservation>(AiEvent.Observation, OnObservation);
        EventManager.RemoveEvent<SCAiFeedback>(ClientEvent.OnAiFeedback, OnServerFeedback);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        AiCommandBus.Unbind(AiCommandId.GetMyPlayerName, OnGetPlayerName);
        AiCommandBus.Unbind(AiCommandId.ReadChatMessage, OnReadChat);
        AiCommandBus.Unbind(AiCommandId.ReadEvents, OnReadEvents);
        AiCommandBus.Unbind(AiCommandId.ReloadLuaCommand, OnReloadLua);
        AiCommandBus.Unbind(AiCommandId.ExecLuaCommand, OnRunLuaExtension);
        AiCommandBus.Unbind(AiCommandId.ListCommands, OnListCommands);
        if (Current == this) Current = null;
    }

    // 界面把玩家看到的每一行都转发过来：进缓冲，@自己的再补一条中断
    private void OnObservation(AiObservation observation)
    {
        observations.Append(observation);

        if (observation.Category != AiObservation.ChatCategory || observation.FromSelf) return;
        string self = ClientSelection.playerName;
        if (string.IsNullOrEmpty(self)) return;
        if (observation.Text == null || !observation.Text.Contains("@" + self)) return;
        hub.Push(AiInterruptKind.ChatMention, "内容=" + observation.Text);
    }

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
