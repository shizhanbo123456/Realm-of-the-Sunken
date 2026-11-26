using Ros.Config;
// AI 操控的 Lua 接口：Tick 状态机常驻、决策状态机按命令激活，脚本读写 API 都在这里
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Ros.Transport;
using UnityEngine;
using XLua;

public sealed class AiLuaInterface : ClientSubManager
{
    public static AiLuaInterface Current { get; private set; }

    private const string ScriptFolder = "LuaInterface";
    private const string StateMachineFolder = "StateMachine";
    private const string MonitorFolder = "Monitor";
    private const string ExtensionFolder = "Extensions";
    private const string DefaultMonitorScript = "tick_state_machine.lua";
    private const string DefaultExtensionScript = "extensions.lua";

    private LuaEnv env;          // 当前 LuaEnv 快照，LuaManager 重建后自动切换到新实例
    private bool loaderAdded;
    private bool missingLogged;
    private LuaFunction tickOnTick;         // 常驻 Tick 状态机：盯飘字与 @自己的聊天
    private LuaFunction decisionOnDecide;   // 决策状态机：控制角色
    private bool tickLoaded;
    private bool tickBroken;                // 加载失败过，本次环境不再重试，避免每帧报错
    private bool active;
    private bool extensionsLoaded;
    private string decisionScript; // 当前决策脚本文件名，由 luaSM_start 传入
    private string decisionArgs = string.Empty; // luaSM_start 的第二段参数串，原样交给脚本
    private bool stopRequested; // 脚本自己要求关掉状态机：等 on_decide 返回后再停，避免在执行中 dispose 自己
    private string monitorScript = DefaultMonitorScript;   // 消息监视器：默认启动，只能换脚本
    private string extensionScript = DefaultExtensionScript; // 命令行拓展脚本
    private readonly object[] tickArgs = new object[1];

    private readonly AiLuaApi api;
    private readonly Dictionary<string, LuaFunction> extensions = new();
    private readonly List<string> commandNames = new();
    private readonly List<SCMinimapEntity> mapBuffer = new();

    public AiLuaInterface()
    {
        api = new AiLuaApi(this);
    }

    public bool Active => active;

    public IReadOnlyList<string> CommandNames => commandNames;

    // 脚本放在 StreamingAssets，编辑器与打包后都能按普通文件读改
    private static string ScriptRoot => Path.Combine(Application.streamingAssetsPath, ScriptFolder);

    public override void Init(ClientLogicManager owner)
    {
        base.Init(owner);
        Current = this;
    }

    public override void Tick(float deltaTime)
    {
        if (!EnsureEnv()) return;
        tickArgs[0] = deltaTime;

        if (EnsureTick())
        {
            try
            {
                tickOnTick.Call(tickArgs);
            }
            catch (Exception e)
            {
                Fail(monitorScript, e);
                tickOnTick.Dispose();
                tickOnTick = null;
                tickLoaded = false;
                tickBroken = true;
            }
        }

        if (!active || decisionOnDecide == null) return;

        try
        {
            decisionOnDecide.Call(tickArgs);
        }
        catch (Exception e)
        {
            Fail(decisionScript, e);
            StopStateMachineInternal(true);
            return;
        }

        // 脚本自己喊停：必须在 Call 返回之后再 dispose，否则等于在执行中销毁自己
        if (!stopRequested) return;
        stopRequested = false;
        StopStateMachineInternal(true);
    }

    protected override void UnbindEvents()
    {
        ResetScriptState();
        if (Current == this) Current = null;
    }

    // LuaEnv 由 LuaManager 持有；实例被重建（场景重载 / 退出）时旧脚本句柄全部作废重来
    private bool EnsureEnv()
    {
        var manager = Tool.LuaManager;
        if (manager == null)
        {
            if (!missingLogged)
            {
                missingLogged = true;
                Debug.LogError("[AI][Lua] 场景缺少 LuaManager，Lua 接口不可用");
            }
            return false;
        }
        if (env == manager.Env) return env != null;

        ResetScriptState();
        missingLogged = false;
        env = manager.Env;
        if (env == null) return false;

        if (!loaderAdded)
        {
            env.AddLoader(Loader);
            loaderAdded = true;
        }
        env.Global.Set("ai", api);
        return true;
    }

    private void ResetScriptState()
    {
        active = false;
        extensionsLoaded = false;
        tickLoaded = false;
        tickBroken = false;
        if (tickOnTick != null)
        {
            tickOnTick.Dispose();
            tickOnTick = null;
        }
        ResetDecision();
        ClearCommands();
    }

    private void ResetDecision()
    {
        active = false;
        stopRequested = false;
        if (decisionOnDecide == null) return;
        decisionOnDecide.Dispose();
        decisionOnDecide = null;
    }

    private void ClearCommands()
    {
        foreach (var pair in extensions) pair.Value.Dispose();
        extensions.Clear();
        commandNames.Clear();
    }

    #region 消息监视器（常驻，默认启动，只能换脚本不能关）
    // 常驻脚本只跑一次；加载失败就标记坏掉，不再每帧重试，等 luaMonitor_reload 换
    private bool EnsureTick()
    {
        if (tickLoaded && tickOnTick != null) return true;
        if (tickBroken) return false;

        string text = ReadScript(MonitorFolder, monitorScript);
        if (text == null)
        {
            tickBroken = true;
            return false;
        }

        try
        {
            env.DoString(text, monitorScript);
        }
        catch (Exception e)
        {
            Fail(monitorScript, e);
            tickBroken = true;
            return false;
        }

        if (tickOnTick != null) tickOnTick.Dispose();
        tickOnTick = env.Global.Get<LuaFunction>("on_tick");
        if (tickOnTick == null)
        {
            Debug.LogError("[AI][Lua] " + monitorScript + " 必须定义全局函数 on_tick(dt)");
            tickBroken = true;
            return false;
        }

        tickLoaded = true;
        return true;
    }

    // 换监视器脚本：丢掉旧 on_tick 立即重载，失败也保持「已停止」状态等下一次重载
    public string ReloadMonitor(string luaFile)
    {
        if (!EnsureEnv()) return "错误：LuaManager 未就绪";

        string name = luaFile == null ? string.Empty : luaFile.Trim();
        if (name.Length == 0) return "错误：监视器脚本名为空，用 " + MonitorFolder + " 目录下的文件名";
        string file = ResolveScript(MonitorFolder, name);
        if (file == null) return "错误：找不到监视器脚本 " + name;

        monitorScript = file;
        if (tickOnTick != null)
        {
            tickOnTick.Dispose();
            tickOnTick = null;
        }
        tickLoaded = false;
        tickBroken = false;
        return EnsureTick() ? "已重载消息监视器：" + Path.GetFileNameWithoutExtension(file)
            : "错误：监视器脚本 " + file + " 加载失败（详见控制台）";
    }
    #endregion

    #region 决策状态机
    // 可用的决策脚本 = StateMachine 目录下的全部 .lua，后面跟上脚本自己声明的参数含义
    public List<string> ListStateMachines()
    {
        List<string> names = ListScripts(StateMachineFolder, "状态机脚本");
        var lines = new List<string>(names.Count);
        for (int i = 0; i < names.Count; i++)
        {
            string hint = ReadArgsHint(StateMachineFolder, names[i] + ".lua");
            lines.Add(hint == null ? names[i] : names[i] + " | " + hint);
        }
        return lines;
    }

    // 脚本里用「--@args 说明」一行声明参数含义；没写这行就不加说明
    private string ReadArgsHint(string folder, string file)
    {
        string path = Path.Combine(ScriptRoot, folder, file);
        if (!File.Exists(path)) return null;

        string[] lines = File.ReadAllLines(path, Encoding.UTF8);
        int count = lines.Length < 15 ? lines.Length : 15;
        for (int i = 0; i < count; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith("--@args", StringComparison.Ordinal)) continue;
            string hint = line.Substring("--@args".Length).Trim();
            return hint.Length == 0 ? "无参数" : hint;
        }
        return null;
    }

    // 可用的监视器脚本 = Monitor 目录下的全部 .lua
    public List<string> ListMonitors() => ListScripts(MonitorFolder, "监视器脚本");

    private List<string> ListScripts(string folder, string label)
    {
        var names = new List<string>();
        string path = Path.Combine(ScriptRoot, folder);
        if (!Directory.Exists(path))
        {
            Debug.LogError("[AI][Lua] 找不到" + label + "目录：" + path);
            return names;
        }

        string[] files = Directory.GetFiles(path, "*.lua");
        for (int i = 0; i < files.Length; i++)
        {
            names.Add(Path.GetFileNameWithoutExtension(files[i]));
        }
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    // 激活即接管：只换决策脚本，拓展命令与监视器各管各的，互不重载
    public string ActivateStateMachine(string luaFile, string args)
    {
        if (!EnsureEnv()) return "错误：LuaManager 未就绪";

        string name = luaFile == null ? string.Empty : luaFile.Trim();
        if (name.Length == 0) return "错误：状态机脚本名为空，用 luaSM_list 查可用名字";
        string file = ResolveScript(StateMachineFolder, name);
        if (file == null) return "错误：找不到状态机脚本 " + name;

        ResetDecision();
        // 参数要在脚本执行前就位：chunk 顶层就可能读 ai:start_args()
        decisionScript = file;
        decisionArgs = args == null ? string.Empty : args.Trim();
        if (!LoadDecision(file)) return "错误：状态机脚本 " + file + " 加载失败（详见控制台）";

        active = true;
        return "状态机已激活：" + Path.GetFileNameWithoutExtension(file);
    }

    // luaSM_start 第二段参数串，原样交给脚本（ai:start_args()）
    internal string DecisionArgs => decisionArgs;

    // 脚本自己要求关停：置个标记，等 on_decide 返回后再真正 dispose
    internal void RequestStopSelf() => stopRequested = true;

    // control_* 直接接管时调用：只停决策脚本，不补站立（操控意图马上覆盖）
    public void StopDecision()
    {
        if (!active) return;
        ResetDecision();
    }

    // 名字带不带 .lua 都认，只在指定子目录里找；找不到返回 null
    private string ResolveScript(string folder, string name)
    {
        string file = name.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? name : name + ".lua";
        return File.Exists(Path.Combine(ScriptRoot, folder, file)) ? file : null;
    }

    public string StopStateMachine()
    {
        if (!EnsureEnv()) return "错误：LuaManager 未就绪";
        StopStateMachineInternal(true);
        return "状态机已停止（已下发站立）";
    }

    // 停决策后必须让实体站住：行为层「无请求 = 保持当前动作」，否则角色会一直跑
    private void StopStateMachineInternal(bool sendStand)
    {
        ResetDecision();
        if (!sendStand) return;

        var request = new AiIntentRequest(Vector2.zero, 0);
        bool registered = AiCommandBus.SendIntent(AiIntent.Stand, request);
        if (registered && request.Rejected) Debug.LogError("[AI][Lua] 停止状态机后站立下发失败：" + request.Error);
    }

    private bool LoadDecision(string file)
    {
        string text = ReadScript(StateMachineFolder, file);
        if (text == null) return false;

        try
        {
            env.DoString(text, StateMachineFolder + "/" + file);
        }
        catch (Exception e)
        {
            Fail(StateMachineFolder + "/" + file, e);
            return false;
        }

        decisionOnDecide = env.Global.Get<LuaFunction>("on_decide");
        if (decisionOnDecide != null) return true;

        Debug.LogError("[AI][Lua] " + file + " 必须定义全局函数 on_decide(dt)");
        return false;
    }
    #endregion

    #region 拓展命令
    public void RegisterCommand(string name, LuaFunction fn)
    {
        if (string.IsNullOrEmpty(name))
        {
            Debug.LogError("[AI][Lua] register_command 命令名为空");
            return;
        }
        if (fn == null)
        {
            Debug.LogError("[AI][Lua] register_command 处理函数为空：" + name);
            return;
        }

        if (extensions.TryGetValue(name, out var old))
        {
            old.Dispose();
            extensions.Remove(name);
            commandNames.Remove(name);
        }
        extensions.Add(name, fn);
        commandNames.Add(name);
    }

    public string RunExtension(string name, string argText)
    {
        if (!EnsureEnv()) return "错误：LuaManager 未就绪";
        if (!extensionsLoaded && !LoadExtensions()) return "错误：拓展脚本加载失败（详见控制台）";
        if (!extensions.TryGetValue(name, out var fn))
        {
            return "错误：没有名为 " + name + " 的拓展命令（用 listCommands 查看）";
        }

        object[] result;
        try
        {
            result = fn.Call(argText);
        }
        catch (Exception e)
        {
            Fail("拓展命令 " + name, e);
            return "错误：拓展命令 " + name + " 异常：" + e.Message;
        }

        if (result == null || result.Length == 0 || result[0] == null) return "（无返回）";
        return result[0].ToString();
    }

    // 只重载拓展命令：清空注册表后重跑指定的拓展脚本，不动监视器与状态机
    public string ReloadExtensions(string luaFile)
    {
        if (!EnsureEnv()) return "错误：LuaManager 未就绪";

        string name = luaFile == null ? string.Empty : luaFile.Trim();
        if (name.Length == 0) return "错误：拓展脚本名为空，用 " + ExtensionFolder + " 目录下的文件名";
        string file = ResolveScript(ExtensionFolder, name);
        if (file == null) return "错误：找不到拓展脚本 " + name;

        extensionScript = file;
        ClearCommands();
        extensionsLoaded = false;
        return LoadExtensions() ? "已重载 lua 拓展命令：" + Path.GetFileNameWithoutExtension(file)
            : "错误：拓展脚本 " + file + " 加载失败（详见控制台）";
    }

    private bool LoadExtensions()
    {
        string text = ReadScript(ExtensionFolder, extensionScript);
        if (text == null) return false;

        try
        {
            env.DoString(text, ExtensionFolder + "/" + extensionScript);
            extensionsLoaded = true;
            return true;
        }
        catch (Exception e)
        {
            Fail(ExtensionFolder + "/" + extensionScript, e);
            return false;
        }
    }
    #endregion

    #region 当前脚本
    // 只报「当前持有哪份脚本 + 在不在跑」，不改任何状态
    public string DescribeLoadedScripts()
    {
        var builder = new StringBuilder();

        builder.Append("状态机=");
        if (active) builder.Append(Path.GetFileNameWithoutExtension(decisionScript)).Append("（运行中）");
        else builder.Append("（未激活）");

        builder.Append("\n监视器=").Append(Path.GetFileNameWithoutExtension(monitorScript));
        if (tickLoaded) builder.Append("（常驻运行中）");
        else if (tickBroken) builder.Append("（已停止，等 luaMonitor_reload）");
        else builder.Append("（未加载）");

        builder.Append("\n拓展=").Append(Path.GetFileNameWithoutExtension(extensionScript));
        builder.Append(extensionsLoaded ? "（已加载）" : "（未加载）");

        return builder.ToString();
    }
    #endregion

    #region 环境
    // 重载不清 package.loaded：入口脚本每次全量重新执行，require 的用户模块沿用缓存
    private byte[] Loader(ref string filepath)
    {
        string path = Path.Combine(ScriptRoot, filepath.Replace('.', '/') + ".lua");
        if (!File.Exists(path)) return null;
        return File.ReadAllBytes(path);
    }

    private string ReadScript(string folder, string file)
    {
        string path = Path.Combine(ScriptRoot, folder, file);
        if (!File.Exists(path))
        {
            Debug.LogError("[AI][Lua] 找不到脚本：" + path);
            return null;
        }
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private void Fail(string chunk, Exception e)
    {
        Debug.LogError($"[AI][Lua] {chunk} 执行失败：{e.Message}");

        var commands = AiCommandLine.Current;
        if (commands == null) return;
        commands.Interrupts.PushLua(AiInterruptKind.LuaError, "脚本=" + chunk, "错误=" + e.Message);
    }

    internal List<SCMinimapEntity> MapBuffer => mapBuffer;

    internal void CopyMinimap()
    {
        var battle = Tool.BattleManager;
        if (battle == null)
        {
            mapBuffer.Clear();
            return;
        }
        battle.AiCopyMinimap(mapBuffer);
    }
    #endregion
}

// 暴露给 lua 的 API 表：读状态零成本可每帧调，写动作一律走事件总线（与命令行同一条链路）
public sealed class AiLuaApi
{
    private readonly AiLuaInterface owner;

    private AiIntent lastIntent;
    private Vector2 lastPoint;
    private ushort lastTarget;
    private int lastSelfId;
    private bool hasLast;

    private static BattleManager Battle => Tool.BattleManager;

    private static AiCommandLine Line => AiCommandLine.Current;

    public AiLuaApi(AiLuaInterface owner) { this.owner = owner; }

    #region 读：会话与自身
    // 命令在当前阶段是否可用：与命令行同一口径（看有没有归属方注册了这条命令）
    public bool can(string command)
    {
        var line = Line;
        return line != null && line.Can(command);
    }

    // 任意命令都能从 Lua 调：解析与分发与命令行完全同一条链路，返回回复文本（无回复返回 null）
    public string call(string command, string args)
    {
        var line = Line;
        if (line == null) return "错误：命令行未就绪";
        string text = string.IsNullOrEmpty(args) ? command : command + " " + args;
        return line.Execute(text);
    }

    public bool connected() => NetworkManager.CanSendWorldCommand;

    public int self_id()
    {
        var loading = ClientLoadingManager.Current;
        return loading != null && loading.HasSession ? loading.OwnEntityId : 0;
    }

    public string self_name() => ClientSelection.playerName ?? "";

    public string self_camp()
    {
        var loading = ClientLoadingManager.Current;
        return loading != null && loading.HasSession ? loading.Camp.ToString() : "None";
    }

    public bool self_alive()
    {
        var loading = ClientLoadingManager.Current;
        return loading != null && loading.HasSession && loading.OwnEntityId != 0;
    }

    public int self_hp() => Battle != null ? Battle.AiSelfHealth : 0;

    public int self_max_hp() => Battle != null ? Battle.AiSelfMaxHealth : 0;

    public float self_x() => Battle != null && Battle.AiTryGetSelfPos(out Vector3 pos) ? pos.x : 0f;

    public float self_z() => Battle != null && Battle.AiTryGetSelfPos(out Vector3 pos) ? pos.z : 0f;

    public float self_yaw() => Battle != null ? Battle.AiSelfYaw() : 0f;

    public int buff_count() => Battle != null ? Battle.AiSelfBuffs.Count : 0;

    public string buff(int index)
    {
        var buffs = Battle != null ? Battle.AiSelfBuffs : null;
        if (buffs == null || index < 0 || index >= buffs.Count) return "";
        return buffs[index];
    }
    #endregion

    #region 读：缓冲
    // Tick 状态机靠总数判断有没有新内容，再按增量读最近几条
    public int prompt_total() => Line != null ? Line.PromptTotal : 0;

    public int chat_total() => Line != null ? Line.ChatTotal : 0;

    public string prompt_buffer(int count) => Line != null ? Line.ReadPrompt(count) : "";

    public string chat_recent(int channel, int count) => Line != null ? Line.ReadChat(channel, count) : "";

    public string important_events(int count) => Line != null ? Line.ReadImportant(count) : "";
    #endregion

    #region 读：小地图
    public int map_count()
    {
        owner.CopyMinimap();
        return owner.MapBuffer.Count;
    }

    public int map_id(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count ? list[index].entityId : 0;
    }

    public int map_cat(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count ? (int)list[index].type.category : -1;
    }

    public int map_val(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count ? list[index].type.value : -1;
    }

    public int map_camp(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count ? (int)list[index].camp : 0;
    }

    public float map_x(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count ? list[index].posX : 0f;
    }

    public float map_z(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count ? list[index].posZ : 0f;
    }

    public bool map_marked(int index)
    {
        var list = owner.MapBuffer;
        return index >= 0 && index < list.Count && list[index].marked;
    }
    #endregion

    #region 读：技能
    public int slot_count()
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        return skills != null ? skills.ActiveSkills.Count : 0;
    }

    public int slot_skill(int slot)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        return skills != null ? skills.GetSlotSkillId(slot) : -1;
    }

    public float slot_progress(int slot)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null) return 0f;
        int skillId = skills.GetSlotSkillId(slot);
        return skillId < 0 ? 0f : skills.Recharge.GetCycleProgress(skillId);
    }

    public int slot_store(int slot)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null) return 0;
        int skillId = skills.GetSlotSkillId(slot);
        return skillId < 0 ? 0 : skills.Recharge.GetStore(skillId);
    }

    public int held_count()
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        return skills != null ? skills.HeldSkills.Count : 0;
    }

    public int held_skill(int index)
    {
        var skills = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;
        if (skills == null || index < 0 || index >= skills.HeldSkills.Count) return -1;
        return skills.HeldSkills[index];
    }

    // 技能配表（skill.csv）静态信息：id / 名称 / 品质 / 射程 / 夹角 / 自动索敌 / CD / 存储上限；未配置返回空串
    // 射程 = 技能能影响到的距离，Lua 据此判断当前能否打到目标
    public string skill_info(int skillId)
    {
        if (!SkillConfig.content.TryGetValue(skillId, out var cfg)) return "";

        var builder = new StringBuilder();
        builder.Append("id=").Append(cfg.key)
            .Append(" 名称=").Append(cfg.name)
            .Append(" 品质=").Append(string.IsNullOrEmpty(cfg.quality) ? "无" : cfg.quality)
            .Append(" 射程=").Append(cfg.castRange.ToString("0.##"))
            .Append(" 夹角=").Append(cfg.targetSector.ToString("0.##"))
            .Append(" 自动索敌=").Append((cfg.autoTarget ?? false) ? 1 : 0)
            .Append(" CD=").Append(cfg.cd.ToString("0.##"))
            .Append(" 存储上限=").Append(cfg.storeLimit);
        return builder.ToString();
    }
    #endregion

    #region 读：守护点与昼夜
    // 0~100 = 被打掉的血量百分比（AI 侧口径），-1 = 还没收到守护点数据
    public int guard_progress(int index)
    {
        if (Battle == null || !Battle.AiHasGuardSnapshot) return -1;
        var info = Battle.AiGuardSnapshot;
        switch (index)
        {
            case 0: return GlobalConfig.GuardPointDamagedPercent(0, info.mainHealth, info.healthScale);
            case 1: return GlobalConfig.GuardPointDamagedPercent(1, info.sub1Health, info.healthScale);
            case 2: return GlobalConfig.GuardPointDamagedPercent(2, info.sub2Health, info.healthScale);
            case 3: return GlobalConfig.GuardPointDamagedPercent(3, info.sub3Health, info.healthScale);
            default: return -1;
        }
    }

    public bool is_night() => !BattleManager.IsDay;

    // 当前周期时间：0~2 单调递增，0/2 午夜、1 正午、白天是 0.5~1.5
    // 不要拿折返量当进度（会 0→1→0 来回跑）；判断昼夜直接看 is_night()
    public float cycle_time() => BattleManager.CycleTime;
    #endregion

    #region 写：意图与动作
    public bool stand() => SendIntent(AiIntent.Stand, Vector2.zero, 0);

    public bool stop() => SendIntent(AiIntent.Stop, Vector2.zero, 0);

    public bool move_to(float x, float z) => SendIntent(AiIntent.MoveTo, new Vector2(x, z), 0);

    public bool attack(int entityId)
    {
        if (entityId <= 0) return false;
        return SendIntent(AiIntent.AttackTarget, Vector2.zero, (ushort)entityId);
    }

    public bool signal(int messageId) => CallCommand(ai_commandEnum.quickMessage_send, messageId);

    public bool signal_at(int messageId, float x, float z) =>
        CallCommand(ai_commandEnum.quickMessage_sendPosition, messageId, x, z);

    public void interrupt(string text)
    {
        var commands = Line;
        if (commands == null) return;
        commands.Interrupts.PushLua(AiInterruptKind.LuaTrigger, string.IsNullOrEmpty(text) ? "lua 触发" : text);
    }

    // luaSM_start 第二段参数串，原样返回（形如 "75 87"，没给就是空串）
    public string start_args() => owner.DecisionArgs;

    // 状态机自己收工：等本次 on_decide 返回后由 C# 停掉（不会在执行中销毁自己）
    public void stop_self() => owner.RequestStopSelf();

    // 同值意图不重发（服务器侧转发器每拍重申，同值重发是纯浪费）；复活换实体时缓存作废
    private bool SendIntent(AiIntent intent, Vector2 position, ushort targetId)
    {
        if (!NetworkManager.CanSendWorldCommand) return false;

        int selfId = self_id();
        if (selfId != lastSelfId)
        {
            lastSelfId = selfId;
            hasLast = false;
        }

        bool same = hasLast && intent == lastIntent && targetId == lastTarget
            && (intent != AiIntent.MoveTo || (position - lastPoint).sqrMagnitude < 0.01f);
        if (same) return true;

        var request = new AiIntentRequest(position, targetId);
        if (!AiCommandBus.SendIntent(intent, request)) return false;
        if (request.Rejected)
        {
            Debug.LogError("[AI][Lua] 意图下发失败：" + request.Error);
            return false;
        }

        lastIntent = intent;
        lastPoint = position;
        lastTarget = targetId;
        hasLast = true;
        return true;
    }

    // 有配表命令的动作（放技能、快捷消息）走命令行同一条链路，可用性也跟着命令注册走
    private static bool CallCommand(ai_commandEnum command, params object[] values)
    {
        if (!AiCommandBus.Ready(command)) return false;

        AiCommandLog.LuaCall(command.ToString(), values);
        string reply = null;
        AiCommandBus.Call(command, new AiRequest(command.ToString(), values, text => reply = text));
        if (!string.IsNullOrEmpty(reply)) AiCommandLog.LuaOut(reply);
        if (reply == null || !reply.StartsWith("错误：")) return true;

        Debug.LogError("[AI][Lua] 命令下发失败：" + reply);
        return false;
    }
    #endregion

    #region 写：拓展命令注册
    public void register_command(string name, LuaFunction fn)
    {
        owner.RegisterCommand(name, fn);
    }

    public int command_count() => owner.CommandNames.Count;

    public string command_name(int index)
    {
        return index >= 0 && index < owner.CommandNames.Count ? owner.CommandNames[index] : "";
    }
    #endregion
}
