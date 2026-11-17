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

    public LobbyPage(UI_LobbyPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    //结算在大厅之上播放期间隐藏/恢复面板
    public void SetPanelVisible(bool visible) => Root.visible = visible;

    public override void Construct()
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
        EventManager.AddEvent<SCLobbyInfo>(ClientEvent.OnLobbyInfoUpdate, OnLobbyInfoUpdate);
        // 有缓存立即回显（服务器只在事件时广播，错过就没了）
        ApplyLobbyInfo(NetworkManager.LatestLobbyInfo);
        // 大厅展示真人成员所选角色的场景模型（战斗页 Enter 时会隐藏，故返回大厅需重建）
        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.ResetLobbyView();
    }

    public override void Exit()
    {
        base.Exit();
        EventManager.RemoveEvent<SCLobbyInfo>(ClientEvent.OnLobbyInfoUpdate, OnLobbyInfoUpdate);
    }

    #region//Local
    private void WireCamp(UI_LobbCampOperation block, PlayerCampType camp)
    {
        block.m_join.onClick.Add(() => JoinCamp(camp));
        block.m_quit.onClick.Add(() => JoinCamp(PlayerCampType.Spectator));
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

        if (info?.members != null)
        {
            for (int i = 0; i < info.members.Count; i++)
            {
                var member = info.members[i];
                if (member == null) continue;
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
        }

        attackRobot = info != null ? info.attackRobotCount : 0;
        defenseRobot = info != null ? info.defenseRobotCount : 0;

        RefreshCampBlock(panel.m_btn_joinAttack, PlayerCampType.Attack, attackRobot);
        RefreshCampBlock(panel.m_btn_joinDefense, PlayerCampType.Defense, defenseRobot);
        panel.m_btn_prepare.title = myReady ? "取消准备" : "准备";
        panel.m_spectate_current_member_count.text = SpectateCount(info).ToString();

        if (Tool.ClientLogicManager != null && Tool.ClientLogicManager.HomePreview != null)
            Tool.ClientLogicManager.HomePreview.RefreshLobby(attackerIndices, defenserIndices);
    }

    private void RefreshCampBlock(UI_LobbCampOperation block, PlayerCampType camp, int aiCount)
    {
        block.m_quit_mode.selectedIndex = myCamp == camp ? 1 : 0;
        block.m_label_AI_count.text = $"x{aiCount}";
    }

    private static int SpectateCount(SCLobbyInfo info)
    {
        int count = 0;
        if (info?.members == null) return 0;
        for (int i = 0; i < info.members.Count; i++)
        {
            var member = info.members[i];
            if (member != null && member.camp == (int)PlayerCampType.Spectator) count++;
        }
        return count;
    }

    private void JoinCamp(PlayerCampType camp)
    {
        if (BlockedByReady()) return;
        if (camp != myCamp && !HasHumanSeat(camp)) return;
        myCamp = camp;
        RaiseState();
    }

    // 真人名额（不含 AI）：观战不限，按服务器上次广播的成员列表预判，最终以服务器为准
    private bool HasHumanSeat(PlayerCampType camp)
    {
        if (camp == PlayerCampType.Spectator) return true;
        bool attack = camp == PlayerCampType.Attack;
        int limit = attack
            ? GlobalVariable.I(global_variablesEnum.player_limit_attacker)
            : GlobalVariable.I(global_variablesEnum.player_limit_defenser);
        int count = attack ? humanAttack : humanDefense;
        if (count < limit) return true;
        if (Tool.UIManager != null)
            Tool.UIManager.ShowFlyText(attack ? promptEnum.attacker_full : promptEnum.defense_full);
        return false;
    }

    private void ChangeRobotCount(PlayerCampType camp, int delta)
    {
        if (camp == PlayerCampType.Spectator || BlockedByReady()) return;
        bool defense = camp == PlayerCampType.Defense;
        int limit = defense
            ? GlobalVariable.I(global_variablesEnum.player_limit_defenser_AI)
            : GlobalVariable.I(global_variablesEnum.player_limit_attacker_AI);
        int current = defense ? defenseRobot : attackRobot;
        if (delta > 0 && current >= limit)
        {
            if (Tool.UIManager != null)
                Tool.UIManager.ShowFlyText(defense ? promptEnum.defense_ai_full : promptEnum.attack_ai_full);
            return;
        }
        int next = Mathf.Clamp(current + delta, 0, limit);
        if (defense) defenseRobot = next;
        else attackRobot = next;
        RefreshCampBlock(defense ? panel.m_btn_joinDefense : panel.m_btn_joinAttack, camp, next);
        RaiseState();
    }

    // 已准备（含开战倒计时中）不允许改阵营与 AI 数量：服务器只在准备状态变化时重算开局条件
    private bool BlockedByReady()
    {
        if (!myReady) return false;
        if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.cancel_ready_first);
        return true;
    }

    private void ToggleReady()
    {
        myReady = !myReady;
        panel.m_btn_prepare.title = myReady ? "取消准备" : "准备";
        RaiseState();
    }

    private void OnExitClicked()
    {
        // 断开不可逆，先弹二次确认
        if (Tool.UIManager != null) Tool.UIManager.ShowConfirm("确定断开与服务器的连接？", DoExitWorld);
        else DoExitWorld();
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
