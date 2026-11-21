using System.Collections.Generic;
using FairyGUI;
using Ros.Transport;
using Ros.UI.Main;
using UnityEngine;

using Ros.Config;
// 组队大厅：两块阵营操作（加入/退出 + AI 数量）+ 准备/退出 + 观战人数；成员头像列表在新版 UI 里已去掉
public class LobbyPage : PageBase
{
    private readonly UI_LobbyPanel panel;

    private readonly List<int> attackerIndices = new(); // 场景摆位：真人攻方所选角色
    private readonly List<int> defenserIndices = new();  // 场景摆位：真人守方所选角色

    private PlayerCampType myCamp = PlayerCampType.Spectator;
    private bool myReady;
    private int attackRobot;   // 进攻方人机数量（不是 AI 接管人数）
    private int defenseRobot;  // 防守方人机数量
    private int humanAttack;
    private int humanDefense;
    private bool aiBound;      // 本页的 AI 命令是否已注册

    public LobbyPage(UI_LobbyPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.lobby;

    protected override void Init()
    {
        // m_quit_mode：0 = 未加入（显示加入）/ 1 = 已加入（显示退出）
        WireCamp(panel.m_btn_joinAttack, PlayerCampType.Attack);
        WireCamp(panel.m_btn_joinDefense, PlayerCampType.Defense);
        panel.m_btn_prepare.onClick.Add(ToggleReady);
        panel.m_btn_exit.onClick.Add(OnExitClicked);
        panel.m_btn_settings.onClick.Add(OpenSettings);
    }

    // 设置面板是全局子界面，三个页面共用（UIManager 持有）
    private static void OpenSettings()
    {
        if (Tool.UIManager != null) Tool.UIManager.OpenSettings();
    }

    public override void Enter(ShowParam param)
    {
        base.Enter(param);
        Tool.EnvironmentManager.SetTime(GlobalConfig.lobby_env_time); // 固定大厅昼夜，不沿用对局结束时的时刻
        EventManager.AddEvent<SCLobbyInfo>(ClientEvent.OnLobbyInfoUpdate, OnLobbyInfoUpdate);
        // 有缓存立即回显（服务器只在事件时广播，错过就没了）
        if (NetworkManager.HasLobbyInfo) ApplyLobbyInfo(NetworkManager.LatestLobbyInfo);
        else ApplyLobbyInfo(new SCLobbyInfo { members = new List<SCLobbyInfo.LobbyMemberInfo>() });
        // 大厅展示真人成员所选角色的场景模型（战斗页 Enter 时会隐藏，故返回大厅需重建）
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetLobbyView();
        BindAiCommands();
    }

    public override void Exit()
    {
        base.Exit();
        EventManager.RemoveEvent<SCLobbyInfo>(ClientEvent.OnLobbyInfoUpdate, OnLobbyInfoUpdate);
        ResignAiCommands();
    }

    public override void Shutdown() => ResignAiCommands();

    // 大厅阶段对 AI 开放的四条命令：进出本页时挂/摘
    private void BindAiCommands()
    {
        if (aiBound) return;
        aiBound = true;
        AiCommandBus.Bind(ai_commandEnum.lobby_join, OnAiJoinCamp);
        AiCommandBus.Bind(ai_commandEnum.lobby_prepare, OnAiPrepare);
        AiCommandBus.Bind(ai_commandEnum.lobby_exit, OnAiExitRoom);
        AiCommandBus.Bind(ai_commandEnum.lobby_robot, OnAiSetRobotCount);
        AiCommandBus.Bind(ai_commandEnum.lobby_info, OnAiLobbyInfo);
    }

    private void ResignAiCommands()
    {
        if (!aiBound) return;
        aiBound = false;
        AiCommandBus.Unbind(ai_commandEnum.lobby_join, OnAiJoinCamp);
        AiCommandBus.Unbind(ai_commandEnum.lobby_prepare, OnAiPrepare);
        AiCommandBus.Unbind(ai_commandEnum.lobby_exit, OnAiExitRoom);
        AiCommandBus.Unbind(ai_commandEnum.lobby_robot, OnAiSetRobotCount);
        AiCommandBus.Unbind(ai_commandEnum.lobby_info, OnAiLobbyInfo);
    }

    #region AI 命令（与玩家点界面走同一条链路）
    private void OnAiJoinCamp(AiRequest request)
    {
        int camp = request.Int(0);
        if (camp < 0 || camp > 2)
        {
            request.Reject("阵营只能是 0 进攻 / 1 防守 / 2 观战");
            return;
        }

        var reject = JoinCamp((PlayerCampType)camp); // 已准备不许改、真人名额不足会被拦
        if (reject.HasValue)
        {
            request.Reject(reject.Value);
            return;
        }
    }

    // 与准备按钮同一个入口：状态不一致时才切，页面里的准备态始终跟着服务器走
    private void OnAiPrepare(AiRequest request)
    {
        bool want = request.Bool(0);
        if (want != myReady) ToggleReady();
    }

    private void OnAiExitRoom(AiRequest request)
    {
        DoExitWorld(); // 玩家那条路径会先弹二次确认，AI 直接退
    }

    private void OnAiSetRobotCount(AiRequest request)
    {
        int camp = request.Int(0);
        int count = request.Int(1);
        if (camp != 0 && camp != 1)
        {
            request.Reject("camp 只能是 0 进攻方 / 1 防守方");
            return;
        }

        bool defense = camp == 1;
        PlayerCampType side = PlayerCampType.Attack;
        if (defense) side = PlayerCampType.Defense;
        int limit = RobotLimit(side);
        if (count < 0 || count > limit)
        {
            string campName = "进攻";
            if (defense) campName = "防守";
            request.Reject($"{campName}方机器人数范围 0~{limit}");
            return;
        }

        var reject = SetRobotCount(side, count);
        if (reject.HasValue)
        {
            request.Reject(reject.Value);
            return;
        }
    }

    // 当前所在阵营 + 双方真人/人机 + 观战人数
    private void OnAiLobbyInfo(AiRequest request)
    {
        int spectator = 0;
        if (NetworkManager.HasLobbyInfo) spectator = SpectateCount(NetworkManager.LatestLobbyInfo);
        string readyText = "否";
        if (myReady) readyText = "是";
        request.Reply($"我的阵营={myCamp} 准备={readyText}\n"
                      + $"进攻 真人={humanAttack} 人机={attackRobot} 上限={RobotLimit(PlayerCampType.Attack)}\n"
                      + $"防守 真人={humanDefense} 人机={defenseRobot} 上限={RobotLimit(PlayerCampType.Defense)}\n"
                      + $"观战={spectator}");
    }
    #endregion

    #region//Local
    private void WireCamp(UI_LobbCampOperation block, PlayerCampType camp)
    {
        block.m_join.onClick.Add(() => JoinCampClicked(camp));
        block.m_quit.onClick.Add(() => JoinCampClicked(PlayerCampType.Spectator));
        block.m_add_ai.onClick.Add(() => ChangeRobotCount(camp, +1));
        block.m_remove_ai.onClick.Add(() => ChangeRobotCount(camp, -1));
    }

    private void OnLobbyInfoUpdate(SCLobbyInfo info) => ApplyLobbyInfo(info);

    private void ApplyLobbyInfo(SCLobbyInfo info)
    {
        attackerIndices.Clear();
        defenserIndices.Clear();
        humanAttack = 0;
        humanDefense = 0;
        myCamp = PlayerCampType.Spectator;
        myReady = false;

        for (int i = 0; i < info.members.Count; i++)
        {
            var member = info.members[i];
            // 场景只摆真人所选角色：AI 与未选角色不占锚点
            if (member.clientId >= 0 && member.characterIndex >= 0)
            {
                if (member.camp == (int)PlayerCampType.Attack) { attackerIndices.Add(member.characterIndex); humanAttack++; }
                else if (member.camp == (int)PlayerCampType.Defense) { defenserIndices.Add(member.characterIndex); humanDefense++; }
            }
            if (member.clientId != NetworkManager.LocalToken) continue;
            myCamp = (PlayerCampType)member.camp;
            myReady = member.ready;
        }

        attackRobot = info.attackRobotCount;
        defenseRobot = info.defenseRobotCount;

        RefreshCampBlock(panel.m_btn_joinAttack, PlayerCampType.Attack, attackRobot);
        RefreshCampBlock(panel.m_btn_joinDefense, PlayerCampType.Defense, defenseRobot);
        panel.m_btn_prepare.selected = myReady; // 选中态跟随服务器，避免残留上一局的已准备
        panel.m_btn_prepare.title = PrepareButtonTitle();
        panel.m_spectate_current_member_count.text = SpectateCount(info).ToString();

        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.RefreshLobby(attackerIndices, defenserIndices);
    }

    private void RefreshCampBlock(UI_LobbCampOperation block, PlayerCampType camp, int aiCount)
    {
        int quitMode = 0;
        if (myCamp == camp) quitMode = 1;
        block.m_quit_mode.selectedIndex = quitMode;
        block.m_label_AI_count.text = $"x{aiCount}";
    }

    private static int SpectateCount(SCLobbyInfo info)
    {
        int count = 0;
        for (int i = 0; i < info.members.Count; i++)
        {
            var member = info.members[i];
            if (member.camp == (int)PlayerCampType.Spectator) count++;
        }
        return count;
    }

    // 阵营唯一的切换入口：大厅按钮与 AI 的加入阵营命令共用，返回拦截原因（null = 已提交）
    private promptEnum? JoinCamp(PlayerCampType camp)
    {
        if (myReady) return promptEnum.cancel_ready_first; // 已准备（含开战倒计时）不允许改阵营：服务器只在准备状态变化时重算开局条件
        if (camp != myCamp && !HasHumanSeat(camp))
        {
            if (camp == PlayerCampType.Attack) return promptEnum.attacker_full;
            return promptEnum.defense_full;
        }
        myCamp = camp;
        RaiseState();
        return null;
    }

    private void JoinCampClicked(PlayerCampType camp)
    {
        var reject = JoinCamp(camp);
        if (reject.HasValue && Tool.UIManager != null) Tool.UIManager.ShowFlyText(reject.Value);
    }

    // 真人名额（不含 AI）：观战不限，按服务器上次广播的成员列表预判，最终以服务器为准
    private bool HasHumanSeat(PlayerCampType camp)
    {
        if (camp == PlayerCampType.Spectator) return true;
        bool attack = camp == PlayerCampType.Attack;
        int limit;
        if (attack) limit = GlobalVariable.I(global_variablesEnum.player_limit_attacker);
        else limit = GlobalVariable.I(global_variablesEnum.player_limit_defenser);
        int human = humanDefense;
        if (attack) human = humanAttack;
        return human < limit;
    }

    // 机器人数上限（global_variables）
    private static int RobotLimit(PlayerCampType camp)
    {
        if (camp == PlayerCampType.Defense) return GlobalVariable.I(global_variablesEnum.player_limit_defenser_AI);
        return GlobalVariable.I(global_variablesEnum.player_limit_attacker_AI);
    }

    // 机器人数唯一的写入口：大厅 +/- 与 AI 的设置机器人数命令共用，返回拦截原因（null = 已提交）
    private promptEnum? SetRobotCount(PlayerCampType camp, int count)
    {
        if (camp != PlayerCampType.Attack && camp != PlayerCampType.Defense)
            throw new System.ArgumentOutOfRangeException(nameof(camp), camp, "只有进攻/防守阵营有机器人数");
        if (count < 0)
            throw new System.ArgumentOutOfRangeException(nameof(count), count, "机器人数不能为负");
        if (myReady) return promptEnum.cancel_ready_first; // 与改阵营同规则：已准备不再重算开局

        bool defense = camp == PlayerCampType.Defense;
        int limit = RobotLimit(camp);
        if (count > limit)
        {
            if (defense) return promptEnum.defense_ai_full;
            return promptEnum.attack_ai_full;
        }

        if (defense) defenseRobot = count;
        else attackRobot = count;
        UI_LobbCampOperation block = panel.m_btn_joinAttack;
        if (defense) block = panel.m_btn_joinDefense;
        RefreshCampBlock(block, camp, count);
        RaiseState();
        return null;
    }

    private void ChangeRobotCount(PlayerCampType camp, int delta)
    {
        if (camp == PlayerCampType.Spectator) return; // 观战块没有 AI 数量按钮
        int current = attackRobot;
        if (camp == PlayerCampType.Defense) current = defenseRobot;
        if (current <= 0 && delta < 0) return; // 已经 0 了，减号无动作
        // 目标值直接交给唯一写入口判定：超上限给「已满」，已准备给「先取消准备」
        var reject = SetRobotCount(camp, current + delta);
        if (reject.HasValue && Tool.UIManager != null) Tool.UIManager.ShowFlyText(reject.Value);
    }

    private void ToggleReady()
    {
        myReady = !myReady;
        panel.m_btn_prepare.title = PrepareButtonTitle();
        RaiseState();
    }

    private void OnExitClicked()
    {
        // 断开不可逆，先弹二次确认
        if (Tool.UIManager != null) Tool.UIManager.ShowConfirm(MessageVariable.V(message_variablesEnum.lobby_confirmDisconnect), DoExitWorld);
        else DoExitWorld();
    }

    private string PrepareButtonTitle()
    {
        if (myReady) return MessageVariable.V(message_variablesEnum.lobby_btnCancelPrepare);
        return MessageVariable.V(message_variablesEnum.lobby_btnPrepare);
    }

    private void RaiseState()
    {
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendLobbyUpdate(new CSLobbyUpdate()
        {
            camp = (int)myCamp,
            ready = myReady,
            attackRobotCount = attackRobot,
            defenseRobotCount = defenseRobot,
        });
    }

    private static void DoExitWorld()
    {
        if (Tool.NetworkManager != null) Tool.NetworkManager.ExitWorld();
    }
    #endregion
}
