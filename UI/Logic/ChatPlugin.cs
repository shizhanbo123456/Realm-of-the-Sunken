using System.Collections.Generic;
using FairyGUI;
using Ros.Transport;
using Ros.UI.Main;
using UnityEngine;

using Ros.Config;
//全局聊天面板：联网后常驻显示，大厅与战斗共用；离开房间清空并隐藏
public class ChatPlugin
{
    public const int TabSystem = 3; // 页签 = 频道值 0/1/2，3 是本地系统消息页签（不上协议）

    // GM 开关「敌方对话」：开启后阵营频道额外显示敌方阵营的消息
    public static bool ShowEnemyDialog;

    private const int MaxEntries = 50;      // 聊天只保留最近 50 条
    private const float SendInterval = 1f;  // 最快 1s 一条

    private class ChatEntry
    {
        public int channel;        // ChatChannel 值，页签按它过滤
        public bool isSelf;
        public int senderClientId;
        public int camp;           // 发送方阵营：0 进攻 / 1 防守 / 2 观战
        public string name;
        public string content;
    }

    private readonly UI_ChatPanel panel;
    private readonly List<ChatEntry> entries = new();
    private readonly List<ChatEntry> visibleChat = new(); // 当前页签下可见的条目（渲染器按索引读取）
    private readonly List<string> systemEntries = new();  // 系统消息纯文本，整局常驻，战斗结束后清空
    private int viewTab = (int)ChatChannel.All;
    private int sendChannel = (int)ChatChannel.All;
    private float lastSendTime = -999f;
    private float uiScale = 1f;

    public ChatPlugin(UI_ChatPanel panel)
    {
        this.panel = panel;
        GRoot.inst.AddChild(panel);
        panel.visible = false;
    }

    public void Construct()
    {
        panel.m_content.itemProvider = ItemUrlAt;
        panel.m_content.itemRenderer = RenderItem;
        panel.m_input.onSubmit.Add(TrySend);
        panel.m_btn_allChannel.onClick.Add(() => SelectTab((int)ChatChannel.All));
        panel.m_btn_campChannel.onClick.Add(() => SelectTab((int)ChatChannel.Camp));
        panel.m_btn_nearby.onClick.Add(() => SelectTab((int)ChatChannel.Nearby));
        panel.m_btn_systemInfo.onClick.Add(() => SelectTab(TabSystem));

        EventManager.AddEvent(ClientEvent.OnConnect, OnConnect);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.AddEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        EventManager.AddEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.AddEvent<SCChat>(ClientEvent.OnChatMessage, OnChatMessage);
        EventManager.AddEvent<SCStateReport>(ClientEvent.OnStateReport, OnStateReport);
        EventManager.AddEvent<SCQuickMessage>(ClientEvent.OnQuickMessage, OnQuickMessage);
        EventManager.AddEvent(ClientEvent.OnExitWorld, OnLeaveRoom);
        EventManager.AddEvent(ClientEvent.OnRestartGame, OnLeaveRoom);
    }

    public void Shutdown()
    {
        //EventManager 是静态的，订阅必须与解绑成对
        EventManager.RemoveEvent(ClientEvent.OnConnect, OnConnect);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleEnd, OnBattleEnd);
        EventManager.RemoveEvent<SCBattleEvent>(ClientEvent.OnBattleEvent, OnBattleEvent);
        EventManager.RemoveEvent<SCChat>(ClientEvent.OnChatMessage, OnChatMessage);
        EventManager.RemoveEvent<SCStateReport>(ClientEvent.OnStateReport, OnStateReport);
        EventManager.RemoveEvent<SCQuickMessage>(ClientEvent.OnQuickMessage, OnQuickMessage);
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, OnLeaveRoom);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, OnLeaveRoom);
    }

    //与页面同规则的整体缩放（设计高度 1080）；贴屏幕左下角
    public void OnResize(float uiScale)
    {
        this.uiScale = uiScale;
        ApplyTransform();
    }

    #region//Local
    private void ApplyTransform()
    {
            panel.scale = new Vector2(uiScale, uiScale);
            panel.SetXY(0f, GRoot.inst.height - panel.height * uiScale); // x 贴左边缘，y 贴屏幕底
    }

    private void OnConnect()
    {
        ClearMessages(); // 显示前先清空，避免残留 FGUI 设计期的测试条目
        SelectTab((int)ChatChannel.All);
        panel.m_show_channel_menu.selectedIndex = 0; // 组队界面不显示频道页签
        panel.visible = true;
    }

    private void OnBattleStart(int camp)
    {
        SelectTab((int)ChatChannel.All);
        panel.m_show_channel_menu.selectedIndex = 1; // 战斗时显示频道页签
    }

    private void OnBattleEnd(int gameState)
    {
        if (systemEntries.Count == 0) return;
        systemEntries.Clear(); // 系统消息只属于本局
        if (viewTab == TabSystem) RefreshList();
    }

    private void OnLeaveRoom()
    {
        ClearMessages();
        SelectTab((int)ChatChannel.All);
        panel.visible = false;
    }

    private void ClearMessages()
    {
        entries.Clear();
        systemEntries.Clear();
        RefreshList();
        panel.m_input.text = string.Empty;
    }

    // 页签即频道，系统消息页签只管看，不参与发送频道
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

    private void TrySend()
    {
        string content = (panel.m_input.text ?? "").Trim();
        // 长度按协议字节数算：StringSerializer.GetLength 含 1 字节长度头，减掉才是正文字节数
        int byteLength = StringSerializer.GetLength(content) - 1;
        int minLength = (int)GlobalVariable.RangeMin(global_variablesEnum.chat_message_length);
        int maxLength = (int)GlobalVariable.RangeMax(global_variablesEnum.chat_message_length);
        if (byteLength < minLength)
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.chat_too_short);
            return;
        }
        if (byteLength > maxLength)
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.chat_too_long);
            return;
        }
        // 附近频道按「操控的实体」算距离，观战/未开战没有实体
        if (sendChannel == (int)ChatChannel.Nearby && OwnEntityId == 0)
        {
            if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.chat_nearby_no_entity);
            return;
        }
        if (Time.time - lastSendTime < SendInterval) return; // 1s 冷却
        lastSendTime = Time.time;

        entries.Add(new ChatEntry()
        {
            channel = sendChannel,
            isSelf = true,
            senderClientId = NetworkManager.LocalToken,
            camp = GetMyCamp(),
            name = GetMyName(),
            content = content,
        });
        RefreshList();
        panel.m_input.text = string.Empty;

        if (Tool.NetworkManager != null)
            Tool.NetworkManager.SendChat(new CSChat() { channel = sendChannel, content = content });
    }

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
        if (chat == null) return;
        if (chat.senderClientId == NetworkManager.LocalToken) return; // 自己的已本地回显
        // 阵营频道客户端过滤；GM「敌方对话」开启时放行敌方阵营消息（附近频道已由服务器按距离筛过）
        if (chat.channel == (int)ChatChannel.Camp && chat.camp != GetMyCamp() && !ShowEnemyDialog) return;
        entries.Add(new ChatEntry()
        {
            channel = chat.channel,
            isSelf = false,
            senderClientId = chat.senderClientId,
            camp = chat.camp,
            name = chat.name,
            content = chat.content,
        });
        RefreshList();
    }

    // 状态上报：协议只带状态、血量、位置与周边敌人数，文本本地拼，统一进阵营频道
    private void OnStateReport(SCStateReport report)
    {
        if (report == null) return;
        if (report.camp != GetMyCamp() && !ShowEnemyDialog) return; // 与聊天同规则：阵营频道只显示同阵营

        bool isSelf = report.senderClientId == NetworkManager.LocalToken; // 自己没有本地回显，靠这条显示
        string head = report.state == (int)EntityReportState.None
            ? $"血量 {report.healthPercent}%"                     // 真人上报不带状态
            : $"{ReportStateText(report.state)}，血量 {report.healthPercent}%";

        entries.Add(new ChatEntry()
        {
            channel = (int)ChatChannel.Camp,
            isSelf = isSelf,
            senderClientId = report.senderClientId,
            camp = report.camp,
            name = GetUnitName(report.senderClientId),
            content = $"{head}，周围敌人 {report.nearbyEnemyCount} 个，{PositionText(report.position, isSelf)}",
        });
        RefreshList();
    }

    // 快捷指令发进阵营频道；chatText 留空的（上报状况）改走 SCStateReport，这里不出条目
    private void OnQuickMessage(SCQuickMessage message)
    {
        if (message == null) return;
        var cfg = QuickMessageConfig.Get(message.messageId);
        if (string.IsNullOrEmpty(cfg.chatText)) return;

        entries.Add(new ChatEntry()
        {
            channel = (int)ChatChannel.Camp,
            isSelf = message.senderClientId == NetworkManager.LocalToken,
            senderClientId = message.senderClientId,
            camp = GetMyCamp(), // 广播只发同阵营，本机阵营即发送方阵营
            name = GetUnitName(message.senderClientId),
            content = cfg.chatText,
        });
        RefreshList();
    }

    // 位置写成「绝对坐标 + 相对读者方位」，自己的上报只给坐标（相对自己无意义）
    private static string PositionText(Vector3 position, bool isSelf)
    {
        string text = $"位置 ({Mathf.RoundToInt(position.x)}, {Mathf.RoundToInt(position.z)})";
        if (isSelf) return text;

        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession || loading.OwnEntityId == 0) return text; // 观战无自身实体：只报坐标
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players == null || !players.TryGetEntityPosition(loading.OwnEntityId, out var mine)) return text;

        float dx = position.x - mine.x;
        float dz = position.z - mine.z;
        return $"{text}，在你{BearingText(dx, dz)}方向 {Mathf.RoundToInt(Mathf.Sqrt(dx * dx + dz * dz))} 米";
    }

    // 世界 +Z 为北、+X 为东，方位按象限写成「北偏东 45°」
    private static string BearingText(float dx, float dz)
    {
        int deg = Mathf.RoundToInt(Mathf.Atan2(dx, dz) * Mathf.Rad2Deg);
        if (deg < 0) deg += 360;
        if (deg >= 360) deg = 0;
        if (deg == 0) return "正北";
        if (deg < 90) return $"北偏东 {deg}°";
        if (deg == 90) return "正东";
        if (deg < 180) return $"南偏东 {180 - deg}°";
        if (deg == 180) return "正南";
        if (deg < 270) return $"南偏西 {deg - 180}°";
        if (deg == 270) return "正西";
        return $"北偏西 {360 - deg}°";
    }

    private static string ReportStateText(int state) => state switch
    {
        (int)EntityReportState.Search => "正在搜索",
        (int)EntityReportState.SearchCombat => "正在清理珊瑚",
        (int)EntityReportState.Chase => "正在追击",
        (int)EntityReportState.Combat => "正在战斗",
        (int)EntityReportState.EscortApproach => "正在前往护送",
        (int)EntityReportState.Escort => "正在护送",
        (int)EntityReportState.EscortCombat => "护送中交战",
        (int)EntityReportState.DangerEvade => "正在躲避危险",
        (int)EntityReportState.DangerClear => "正在搜寻威胁",
        (int)EntityReportState.DangerClearCombat => "正在清除威胁",
        (int)EntityReportState.RallyApproach => "正在前往集结",
        (int)EntityReportState.RallyWait => "正在集结待命",
        (int)EntityReportState.RallyGuard => "正在守卫集结点",
        (int)EntityReportState.Assault => "正在协同进攻",
        (int)EntityReportState.AssaultCombat => "协同进攻中交战",
        _ => "状态未知",
    };

    // 事件类型 → 播报文案：聊天列表里只留纯文本，图标与配色由横幅/飘字承担
    private void OnBattleEvent(SCBattleEvent e)
    {
        if (e == null) return;
        switch (e.type)
        {
            case SCBattleEvent.Type.PlayerKill:
                systemEntries.Add(string.Format(SystemMessageConfig.Get(system_messageEnum.player_kill).text,
                    ResolveEventText(e.textId), ResolveEventText(e.textId2)));
                break;
            case SCBattleEvent.Type.DeathUnattributed:
                systemEntries.Add(string.Format(SystemMessageConfig.Get(system_messageEnum.player_defeated).text,
                    ResolveEventText(e.textId)));
                break;
            case SCBattleEvent.Type.SkillBroadcast:
                return; // 技能播报只进重要事件 banner，不进聊天列表
            default:
                systemEntries.Add(ResolveEventText(e.textId));
                break;
        }
        if (viewTab == TabSystem) RefreshList();
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
        return loading != null && loading.HasSession ? loading.GetName(clientId) : $"玩家{clientId}";
    }

    private void RefreshList()
    {
        RebuildVisibleChat();
        int count = viewTab == TabSystem ? systemEntries.Count : visibleChat.Count;
        // 页签切换会换内容，先回收现有条目，再按 itemProvider 重建
        panel.m_content.RemoveChildrenToPool();
        panel.m_content.numItems = count;
        if (count > 0) panel.m_content.ScrollToView(count - 1);
    }

    private void RebuildVisibleChat()
    {
        visibleChat.Clear();
        if (viewTab == TabSystem) return;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i].channel == viewTab) visibleChat.Add(entries[i]);
        }
    }

    private void RenderItem(int index, GObject obj)
    {
        if (obj is not UI_ChatItem item) return;
        item.m_content.UBBEnabled = true;
        item.m_content.text = viewTab == TabSystem ? SystemLine(index) : ChatLine(index);
    }

    // 频道名 + 发送者 + 内容，三段颜色都取自 InfoManager
    private string ChatLine(int index)
    {
        if (index >= visibleChat.Count) return string.Empty;
        var e = visibleChat[index];
        var info = RequireInfoManager();
        string channel = ChannelLabel(e.channel);
        string name = string.IsNullOrEmpty(e.name) ? $"玩家{e.senderClientId}" : e.name;
        return $"[color=#{info.ChatChannelColorHex}][{channel}][/color] "
             + $"[color=#{info.ChatNameColorHex(e.camp)}]{name}[/color]: "
             + $"[color=#{info.ChatContentColorHex}]{e.content}[/color]";
    }

    private string SystemLine(int index)
    {
        if (index >= systemEntries.Count) return string.Empty;
        var info = RequireInfoManager();
        return $"[color=#{info.ChatChannelColorHex}][系统][/color] [color=#{info.ChatContentColorHex}]{systemEntries[index]}[/color]";
    }

    // 聊天配色的唯一来源：场景里没有 InfoManager 就报出来，不静默用默认色
    private static InfoManager RequireInfoManager()
    {
        if (Tool.InfoManager == null) throw new System.InvalidOperationException("[聊天] 场景缺少 InfoManager，聊天配色取不到");
        return Tool.InfoManager;
    }

    private static string ChannelLabel(int channel) => channel switch
    {
        (int)ChatChannel.All => "全频道",
        (int)ChatChannel.Camp => "阵营",
        (int)ChatChannel.Nearby => "附近",
        _ => throw new System.ArgumentOutOfRangeException(nameof(channel), channel, "聊天频道只有 0 全频道 / 1 阵营 / 2 附近"),
    };

    private int GetMyCamp()
    {
        var loading = ClientLoadingManager.Current;
        if (NetworkManager.BattleRunning && loading != null && loading.HasSession)
        {
            if (loading.Camp == EntityCamp.Attack) return (int)PlayerCampType.Attack;
            if (loading.Camp == EntityCamp.Defense) return (int)PlayerCampType.Defense;
            return (int)PlayerCampType.Spectator; // 观战方玩家（无战斗实体）
        }
        var info = NetworkManager.LatestLobbyInfo;
        if (info == null || info.members == null) return (int)PlayerCampType.Spectator;
        var member = info.members.Find(m => m != null && m.clientId == NetworkManager.LocalToken);
        return member != null ? member.camp : (int)PlayerCampType.Spectator;
    }

    private string GetMyName()
    {
        var loading = ClientLoadingManager.Current;
        if (NetworkManager.BattleRunning && loading != null && loading.HasSession) return loading.GetName(NetworkManager.LocalToken);
        if (NetworkManager.TryGetLobbyPlayerName(NetworkManager.LocalToken, out var lobbyName)) return lobbyName;
        return !string.IsNullOrEmpty(ClientSelection.playerName) ? ClientSelection.playerName : $"玩家{NetworkManager.LocalToken}";
    }
    #endregion
}
