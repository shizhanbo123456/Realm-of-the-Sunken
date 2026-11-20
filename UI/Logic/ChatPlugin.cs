using System.Collections.Generic;
using FairyGUI;
using Ros.Transport;
using Ros.UI.Main;
using UnityEngine;

using Ros.Config;
using Ros.Gm;
//全局聊天面板：联网后常驻显示，大厅与战斗共用；离开房间清空并隐藏
public class ChatPlugin : PageBase
{
    // Submit 的结果：Sent 之外都是拦截原因
    public enum ChatSendResult
    {
        Sent,
        TooShort,
        TooLong,
        NearbyNoEntity,
        TooFrequent,
    }

    public const int TabSystem = 3; // 页签 = 频道值 0/1/2，3 是本地系统消息页签（不上协议）

    // GM 开关「敌方对话」：开启后阵营频道额外显示敌方阵营的消息
    public static bool ShowEnemyDialog;

    private const int MaxEntries = 50;      // 聊天只保留最近 50 条
    private const float SendInterval = 1f;  // 最快 1s 一条

    private class ChatEntry
    {
        public int channel;        // ChatChannel 值，系统消息记 TabSystem；页签按它过滤
        public bool isSelf;
        public int senderClientId;
        public int camp;           // 发送方阵营：0 进攻 / 1 防守 / 2 观战
        public string name;
        public string content;
    }

    private readonly UI_ChatPanel panel;
    private readonly List<ChatEntry> entries = new();
    private readonly List<ChatEntry> visibleChat = new(); // 当前页签下可见的条目（全频道汇总全部；渲染器按索引读取）
    private int viewTab = (int)ChatChannel.All;
    private int sendChannel = (int)ChatChannel.All;
    private float lastSendTime = -999f;
    private bool aiBound; // 本面板的 AI 命令是否已注册

    public ChatPlugin(UI_ChatPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.chat;

    protected override void Init()
    {
        panel.m_content.itemProvider = ItemUrlAt;
        panel.m_content.itemRenderer = RenderItem;
        panel.m_input.onSubmit.Add(TrySend);
        panel.m_btn_allChannel.onClick.Add(() => SelectTab((int)ChatChannel.All));
        panel.m_btn_campChannel.onClick.Add(() => SelectTab((int)ChatChannel.Camp));
        panel.m_btn_nearby.onClick.Add(() => SelectTab((int)ChatChannel.Nearby));
        panel.m_btn_systemInfo.onClick.Add(() => SelectTab(TabSystem));

        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.AddEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        EventManager.AddEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.AddEvent<SCChat>(ClientEvent.OnChatMessage, OnChatMessage);
        EventManager.AddEvent<SCStateReport>(ClientEvent.OnStateReport, OnStateReport);
        EventManager.AddEvent<SCQuickMessage>(ClientEvent.OnQuickMessage, OnQuickMessage);
        EventManager.AddEvent(ClientEvent.OnExitWorld, OnLeaveRoom);
        EventManager.AddEvent(ClientEvent.OnRestartGame, OnLeaveRoom);
    }

    // 联网后由 UIManager 显示：每次显示都清空上一条会话的残留
    public override void Enter(ShowParam param)
    {
        ClearMessages();
        EnterLobbyMode();
        base.Enter(param);
        BindAiCommands();
    }

    public override void Exit()
    {
        base.Exit();
        ResignAiCommands();
    }

    // 聊天面板开着才允许 AI 发言，和玩家一致
    private void BindAiCommands()
    {
        if (aiBound) return;
        aiBound = true;
        AiCommandBus.Bind(ai_commandEnum.chat_send, OnAiSendChat);
    }

    private void ResignAiCommands()
    {
        if (!aiBound) return;
        aiBound = false;
        AiCommandBus.Unbind(ai_commandEnum.chat_send, OnAiSendChat);
    }

    private void OnAiSendChat(AiRequest request)
    {
        int channel = request.Int(0);
        if (channel < 0 || channel > 2)
        {
            request.Reject("channel 只能是 0 全频道 / 1 阵营 / 2 附近");
            return;
        }

        string content = request.Str(1);
        if (content.Length == 0)
        {
            request.Reject("聊天内容不能为空");
            return;
        }
        if (!NetworkManager.CanSendWorldCommand)
        {
            request.Reject("未联网");
            return;
        }

        var result = Submit(channel, content); // 与输入框同一个入口：长度校验、冷却、本地回显都在里面
        if (result != ChatSendResult.Sent)
        {
            var prompt = RejectPrompt(result);
            if (prompt.HasValue) request.Reject(prompt.Value);
            else request.Reject("发送过于频繁，请稍后再试");
            return;
        }
    }

    public override void Shutdown()
    {
        ResignAiCommands();
        //EventManager 是静态的，订阅必须与解绑成对
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        EventManager.RemoveEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.RemoveEvent<SCChat>(ClientEvent.OnChatMessage, OnChatMessage);
        EventManager.RemoveEvent<SCStateReport>(ClientEvent.OnStateReport, OnStateReport);
        EventManager.RemoveEvent<SCQuickMessage>(ClientEvent.OnQuickMessage, OnQuickMessage);
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, OnLeaveRoom);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, OnLeaveRoom);
    }

    #region//Local
    private void OnBattleStart(int camp)
    {
        SelectTab((int)ChatChannel.All);
        panel.m_show_channel_menu.selectedIndex = 1; // 战斗时显示频道页签
    }

    private void OnBattleEnd(int gameState)
    {
        entries.RemoveAll(e => e.channel == TabSystem); // 系统消息只属于本局
        EnterLobbyMode();                                // 结算后回到组队：页签收起、频道回全频道
    }

    // 组队界面：隐藏频道页签，默认显示全频道
    private void EnterLobbyMode()
    {
        panel.m_show_channel_menu.selectedIndex = 0;
        SelectTab((int)ChatChannel.All);
    }

    private void OnLeaveRoom()
    {
        ClearMessages();
        SelectTab((int)ChatChannel.All);
    }

    private void ClearMessages()
    {
        entries.Clear();
        RefreshList();
        panel.m_input.text = string.Empty;
    }

    // 页签即频道；系统页签不参与发送频道（它的输入是 GM 命令）
    private void SelectTab(int tab)
    {
        viewTab = tab;
        if (tab != TabSystem) sendChannel = tab;
        panel.m_btn_allChannel.selected = tab == (int)ChatChannel.All;
        panel.m_btn_campChannel.selected = tab == (int)ChatChannel.Camp;
        panel.m_btn_nearby.selected = tab == (int)ChatChannel.Nearby;
        panel.m_btn_systemInfo.selected = tab == TabSystem;
        RefreshList();
    }

    private string ItemUrlAt(int index) => UI_ChatItem.URL;

    // 输入框与 AI 命令共用的发送入口：校验 → 本地回显 → 发包，返回拦截原因
    private ChatSendResult Submit(int channel, string content)
    {
        content = (content ?? "").Trim();
        // 长度按协议字节数算：StringSerializer.GetLength 含 1 字节长度头，减掉才是正文字节数
        int byteLength = StringSerializer.GetLength(content) - 1;
        int minLength = (int)GlobalVariable.RangeMin(global_variablesEnum.chat_message_length);
        int maxLength = (int)GlobalVariable.RangeMax(global_variablesEnum.chat_message_length);
        if (byteLength < minLength) return ChatSendResult.TooShort;
        if (byteLength > maxLength) return ChatSendResult.TooLong;
        // 附近频道按「操控的实体」算距离，观战/未开战没有实体
        if (channel == (int)ChatChannel.Nearby && OwnEntityId == 0) return ChatSendResult.NearbyNoEntity;
        if (Time.time - lastSendTime < SendInterval) return ChatSendResult.TooFrequent; // 1s 冷却
        lastSendTime = Time.time;

        AppendEntry(new ChatEntry()
        {
            channel = channel,
            isSelf = true,
            senderClientId = NetworkManager.LocalToken,
            camp = GetMyCamp(),
            name = GetMyName(),
            content = content,
        });

        if (Tool.NetworkManager != null)
            Tool.NetworkManager.SendChat(new CSChat() { channel = channel, content = content });
        return ChatSendResult.Sent;
    }

    private void TrySend()
    {
        // 系统页签的输入走 GM 命令，不聊天
        if (viewTab == TabSystem) { TrySendGmCommand(panel.m_input.text); return; }

        ChatSendResult result = Submit(sendChannel, panel.m_input.text);
        if (result == ChatSendResult.Sent)
        {
            panel.m_input.text = string.Empty;
            return;
        }
        // 冷却没有配表文案，输入框这里保持静默，只做本地节流
        promptEnum? prompt = RejectPrompt(result);
        if (prompt.HasValue && Tool.UIManager != null) Tool.UIManager.ShowFlyText(prompt.Value);
    }

    // 系统页签的输入按 GM 命令处理：「命令名 副命令 [参数]」，如 player tp 10,20,30
    private void TrySendGmCommand(string raw)
    {
        string[] tokens = (raw ?? string.Empty).Split(new[] { ' ', '\t', '\u3000' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
        {
            AppendSystem("GM 命令格式：命令名 副命令 [参数]（如 debug delay 200）");
            return;
        }
        if (!GmCommandRunner.TryFind(tokens[0], tokens[1], out var row))
        {
            AppendSystem($"未知 GM 命令：{tokens[0]} {tokens[1]}");
            return;
        }
        if (!GmCommandRunner.Available(row))
        {
            AppendSystem($"{row.subMenuName}：当前相位不可用");
            return;
        }

        string paramText = ParamText(row, tokens, out string error);
        if (error != null) { AppendSystem($"{row.subMenuName} 参数不合法：{error}"); return; }

        if (!GmCommandRunner.TryParseParam(row, paramText, out var arg, out error))
        {
            AppendSystem($"{row.subMenuName} 参数不合法：{error}（输入：{paramText}）");
            return;
        }

        GmCommandRunner.Run(row, arg, AppendSystem);
        panel.m_input.text = string.Empty;
    }

    // 参数段：一条命令只取 tokens[2] 这一个参数，多出来的空格段一律报错，避免拆法有歧义
    private static string ParamText(gm_command row, string[] tokens, out string error)
    {
        error = null;
        int count = tokens.Length - 2;
        if (!row.paramType.HasValue)
        {
            if (count > 0) error = "不接受参数";
            return string.Empty;
        }
        if (count != 1)
        {
            error = count == 0 ? "缺少参数" : ParamHint(row);
            return string.Empty;
        }
        return tokens[2];
    }

    // Vector 的分量必须写在同一个参数段里，不能靠空格拆
    private static string ParamHint(gm_command row)
    {
        bool isVector = row.paramType == GmCommandRunner.ParamVector2 || row.paramType == GmCommandRunner.ParamVector3;
        return isVector ? "只接受一个参数，Vector 写成 (x,y) 或 x,y，不要用空格分隔" : "只接受一个参数";
    }

    // 拦截原因对应的配表提示；冷却不配文案，调用方自行处理
    private static promptEnum? RejectPrompt(ChatSendResult result) => result switch
    {
        ChatSendResult.TooShort => promptEnum.chat_too_short,
        ChatSendResult.TooLong => promptEnum.chat_too_long,
        ChatSendResult.NearbyNoEntity => promptEnum.chat_nearby_no_entity,
        _ => null,
    };

    private static ushort OwnEntityId
    {
        get
        {
            var loading = ClientLoadingManager.Current;
            return loading != null && loading.HasSession ? loading.OwnEntityId : (ushort)0;
        }
    }

    private void OnChatMessage(SCChat chat)
    {
        if (chat.senderClientId == NetworkManager.LocalToken) return; // 自己的已本地回显
        // 阵营频道客户端过滤；GM「敌方对话」开启时放行敌方阵营消息（附近频道已由服务器按距离筛过）
        if (chat.channel == (int)ChatChannel.Camp && chat.camp != GetMyCamp() && !ShowEnemyDialog) return;
        AppendEntry(new ChatEntry()
        {
            channel = chat.channel,
            isSelf = false,
            senderClientId = chat.senderClientId,
            camp = chat.camp,
            name = chat.name,
            content = chat.content,
        });
    }

    // 状态上报：协议只带状态、血量、位置与周边敌人数，文本本地拼，统一进阵营频道
    private void OnStateReport(SCStateReport report)
    {
        if (report.camp != GetMyCamp() && !ShowEnemyDialog) return; // 与聊天同规则：阵营频道只显示同阵营

        bool isSelf = report.senderClientId == NetworkManager.LocalToken; // 自己没有本地回显，靠这条显示
        string head = report.state == (int)EntityReportState.None
            ? MessageVariable.F(message_variablesEnum.chat_health, report.healthPercent) // 真人上报不带状态
            : MessageVariable.F(message_variablesEnum.chat_healthState, ReportStateText(report.state), report.healthPercent);

        AppendEntry(new ChatEntry()
        {
            channel = (int)ChatChannel.Camp,
            isSelf = isSelf,
            senderClientId = report.senderClientId,
            camp = report.camp,
            name = GetUnitName(report.senderClientId),
            content = MessageVariable.F(message_variablesEnum.chat_reportLine, head, report.nearbyEnemyCount, PositionText(report.position, isSelf)),
        });
    }

    // 快捷指令发进阵营频道；chatText 留空的（上报状况）改走 SCStateReport，这里不出条目
    private void OnQuickMessage(SCQuickMessage message)
    {
        var cfg = QuickMessageConfig.Get(message.messageId);
        if (string.IsNullOrEmpty(cfg.chatText)) return;

        AppendEntry(new ChatEntry()
        {
            channel = (int)ChatChannel.Camp,
            isSelf = message.senderClientId == NetworkManager.LocalToken,
            senderClientId = message.senderClientId,
            camp = GetMyCamp(), // 广播只发同阵营，本机阵营即发送方阵营
            name = GetUnitName(message.senderClientId),
            content = cfg.chatText,
        });
    }

    // 位置写成「绝对坐标 + 相对读者方位」，自己的上报只给坐标（相对自己无意义）
    private static string PositionText(Vector3 position, bool isSelf)
    {
        string text = MessageVariable.F(message_variablesEnum.chat_position,
            Mathf.RoundToInt(position.x), Mathf.RoundToInt(position.z));
        if (isSelf) return text;

        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession || loading.OwnEntityId == 0) return text; // 观战无自身实体：只报坐标
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players == null || !players.TryGetEntityPosition(loading.OwnEntityId, out var mine)) return text;

        float dx = position.x - mine.x;
        float dz = position.z - mine.z;
        return MessageVariable.F(message_variablesEnum.chat_distance, text, BearingText(dx, dz),
            Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz)));
    }

    // 世界 +Z 为北、+X 为东，方位按象限写成「北偏东 45°」
    private static string BearingText(float dx, float dz)
    {
        int deg = Mathf.RoundToInt(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
        if (deg < 0) deg += 360;
        if (deg >= 360) deg = 0;
        if (deg == 0) return MessageVariable.V(message_variablesEnum.chat_bearing_north);
        if (deg < 90) return MessageVariable.F(message_variablesEnum.chat_bearing_northEast, deg);
        if (deg == 90) return MessageVariable.V(message_variablesEnum.chat_bearing_east);
        if (deg < 180) return MessageVariable.F(message_variablesEnum.chat_bearing_southEast, 180 - deg);
        if (deg == 180) return MessageVariable.V(message_variablesEnum.chat_bearing_south);
        if (deg < 270) return MessageVariable.F(message_variablesEnum.chat_bearing_southWest, deg - 180);
        if (deg == 270) return MessageVariable.V(message_variablesEnum.chat_bearing_west);
        return MessageVariable.F(message_variablesEnum.chat_bearing_northWest, 360 - deg);
    }

    private static string ReportStateText(int state) => MessageVariable.V(ReportStateKey(state));

    private static message_variablesEnum ReportStateKey(int state) => state switch
    {
        (int)EntityReportState.Search => message_variablesEnum.chat_state_search,
        (int)EntityReportState.SearchCombat => message_variablesEnum.chat_state_searchCombat,
        (int)EntityReportState.Chase => message_variablesEnum.chat_state_chase,
        (int)EntityReportState.Combat => message_variablesEnum.chat_state_combat,
        (int)EntityReportState.EscortApproach => message_variablesEnum.chat_state_escortApproach,
        (int)EntityReportState.Escort => message_variablesEnum.chat_state_escort,
        (int)EntityReportState.EscortCombat => message_variablesEnum.chat_state_escortCombat,
        (int)EntityReportState.DangerEvade => message_variablesEnum.chat_state_dangerEvade,
        (int)EntityReportState.DangerClear => message_variablesEnum.chat_state_dangerClear,
        (int)EntityReportState.DangerClearCombat => message_variablesEnum.chat_state_dangerClearCombat,
        (int)EntityReportState.RallyApproach => message_variablesEnum.chat_state_rallyApproach,
        (int)EntityReportState.RallyWait => message_variablesEnum.chat_state_rallyWait,
        (int)EntityReportState.RallyGuard => message_variablesEnum.chat_state_rallyGuard,
        (int)EntityReportState.Assault => message_variablesEnum.chat_state_assault,
        (int)EntityReportState.AssaultCombat => message_variablesEnum.chat_state_assaultCombat,
        _ => message_variablesEnum.chat_state_unknown,
    };

    // 事件类型 → 播报文案：聊天列表里只留纯文本，图标与配色由横幅/飘字承担
    private void OnBattleEvent(SCBattleEvent e)
    {
        switch (e.type)
        {
            case SCBattleEvent.Type.PlayerKill:
                AppendSystem(string.Format(SystemMessageConfig.Get(system_messageEnum.player_kill).text,
                    ResolveEventText(e.textId), ResolveEventText(e.textId2)));
                break;
            case SCBattleEvent.Type.DeathUnattributed:
                AppendSystem(string.Format(SystemMessageConfig.Get(system_messageEnum.player_defeated).text,
                    ResolveEventText(e.textId)));
                break;
            case SCBattleEvent.Type.SkillBroadcast:
                return; // 技能播报只进重要事件 banner，不进聊天列表
            case SCBattleEvent.Type.NeutralStatueRespawn:
                AppendSystem(string.Format(SystemMessageConfig.Get(system_messageEnum.neutral_statue_respawn).text, e.posX, e.posZ));
                break;
            default:
                AppendSystem(ResolveEventText(e.textId));
                break;
        }
    }

    // 事件文本统一按数字 id 解析：>=PlayerNameBase 映射为玩家名，否则按 system_message 枚举查表
    private static string ResolveEventText(int id)
    {
        if (id >= SCBattleEvent.PlayerNameBase) return GetUnitName(id - SCBattleEvent.PlayerNameBase);
        return SystemMessageConfig.Get((system_messageEnum)id).text;
    }

    private static string GetUnitName(int clientId)
    {
        var loading = ClientLoadingManager.Current;
        return loading != null && loading.HasSession ? loading.GetName(clientId) : MessageVariable.F(message_variablesEnum.default_playerName, clientId);
    }

    private void RefreshList()
    {
        RebuildVisibleChat();
        int count = visibleChat.Count;
        // 页签切换会换内容，先回收现有条目，再按 itemProvider 重建
        panel.m_content.RemoveChildrenToPool();
        panel.m_content.numItems = count;
        if (count > 0) panel.m_content.ScrollToView(count - 1);
    }

    // 写一条聊天：刷新页面 + 转发给 AI（AI 记的就是玩家看到的这一行）
    private void AppendEntry(ChatEntry entry)
    {
        entries.Add(entry);
        RefreshList();
        AiObservation.ReportChat(entry.channel, PlainLine(entry), entry.isSelf);
    }

    // 系统消息与聊天同列表，频道记 TabSystem（客户端本地，不上协议），全频道也会显示
    public void AppendSystem(string text)
    {
        entries.Add(new ChatEntry() { channel = TabSystem, content = text });
        if (viewTab == TabSystem || viewTab == (int)ChatChannel.All) RefreshList();
        AiObservation.ReportChat(TabSystem, "[系统] " + text, false);
    }

    // 转发给 AI 的纯文本：不带 UBB 颜色标签
    private static string PlainLine(ChatEntry entry)
    {
        string name = string.IsNullOrEmpty(entry.name) ? "#" + entry.senderClientId : entry.name;
        return $"[{ChannelLabel(entry.channel)}][{(PlayerCampType)entry.camp}]{name}: {entry.content}";
    }

    // 全频道汇总所有频道（含系统消息）；其余页签只看本频道
    private void RebuildVisibleChat()
    {
        visibleChat.Clear();
        bool all = viewTab == (int)ChatChannel.All;
        for (int i = 0; i < entries.Count; i++)
        {
            if (all || entries[i].channel == viewTab) visibleChat.Add(entries[i]);
        }
    }

    private void RenderItem(int index, GObject obj)
    {
        if (obj is not UI_ChatItem item) return;
        if (index >= visibleChat.Count) { item.m_content.text = string.Empty; return; } // 回收中的旧 item 可能拿到越界索引
        item.m_content.UBBEnabled = true;
        item.m_content.text = visibleChat[index].channel == TabSystem ? SystemLine(index) : ChatLine(index);
    }

    // 频道名 + 发送者 + 内容，三段颜色都取自 InfoManager
    private string ChatLine(int index)
    {
        if (index >= visibleChat.Count) return string.Empty;
        var e = visibleChat[index];
        var info = RequireInfoManager();
        string channel = ChannelLabel(e.channel);
        string name = string.IsNullOrEmpty(e.name) ? MessageVariable.F(message_variablesEnum.default_playerName, e.senderClientId) : e.name;
        return $"[color=#{info.ChatChannelColorHex}][{channel}][/color] "
             + $"[color=#{info.ChatNameColorHex(e.camp)}]{name}[/color]: "
             + $"[color=#{info.ChatContentColorHex}]{e.content}[/color]";
    }

    private string SystemLine(int index)
    {
        if (index >= visibleChat.Count) return string.Empty;
        var info = RequireInfoManager();
        return $"[color=#{info.ChatChannelColorHex}]{MessageVariable.V(message_variablesEnum.chat_tagSystem)}[/color] "
             + $"[color=#{info.ChatContentColorHex}]{visibleChat[index].content}[/color]";
    }

    // 聊天配色的唯一来源：场景里没有 InfoManager 就报出来，不静默用默认色
    private static InfoManager RequireInfoManager()
    {
        if (Tool.InfoManager == null) throw new System.InvalidOperationException("[聊天] 场景缺少 InfoManager，聊天配色取不到");
        return Tool.InfoManager;
    }

    private static string ChannelLabel(int channel) => MessageVariable.V(channel switch
    {
        (int)ChatChannel.All => message_variablesEnum.chat_channel_all,
        (int)ChatChannel.Camp => message_variablesEnum.chat_channel_camp,
        (int)ChatChannel.Nearby => message_variablesEnum.chat_channel_nearby,
        _ => throw new System.ArgumentOutOfRangeException(nameof(channel), channel, "聊天频道只有 0 全频道 / 1 阵营 / 2 附近"),
    });

    public int GetMyCamp()
    {
        var loading = ClientLoadingManager.Current;
        if (NetworkManager.BattleRunning && loading != null && loading.HasSession)
        {
            if (loading.Camp == EntityCamp.Attack) return (int)PlayerCampType.Attack;
            if (loading.Camp == EntityCamp.Defense) return (int)PlayerCampType.Defense;
            return (int)PlayerCampType.Spectator; // 观战方玩家（无战斗实体）
        }
        if (!NetworkManager.HasLobbyInfo) return (int)PlayerCampType.Spectator;
        var info = NetworkManager.LatestLobbyInfo;
        int index = info.members.FindIndex(m => m.clientId == NetworkManager.LocalToken);
        return index >= 0 ? info.members[index].camp : (int)PlayerCampType.Spectator;
    }

    public string GetMyName()
    {
        var loading = ClientLoadingManager.Current;
        if (NetworkManager.BattleRunning && loading != null && loading.HasSession) return loading.GetName(NetworkManager.LocalToken);
        if (NetworkManager.TryGetLobbyPlayerName(NetworkManager.LocalToken, out var lobbyName)) return lobbyName;
        return !string.IsNullOrEmpty(ClientSelection.playerName) ? ClientSelection.playerName : MessageVariable.F(message_variablesEnum.default_playerName, NetworkManager.LocalToken);
    }
    #endregion
}
