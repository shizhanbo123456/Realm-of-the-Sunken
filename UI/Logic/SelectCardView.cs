using System;
using System.Collections.Generic;
using FairyGUI;
using Ros.Config;
using Ros.Transport;
using Ros.UI.Main;
using UnityEngine;

// 抽卡：悬浮按钮 + 抽卡界面 + 选/弃上报，整套状态机都在这里
public class SelectCardView : PageBase
{
    private readonly UI_SelectCardPanel panel;

    // 有次数的档位（渲染用，避免每帧分配）
    private readonly List<int> pendingTiers = new();
    private readonly int[] pending = new int[CardCatalog.TierCount];
    private CardTier currentTier;   // 最近一次打开的档位（弃卡按它上报）
    private bool awaitingSeed;      // 已经发出种子请求、还没收到回包
    private readonly List<CardRoll> currentRolls = new(); // 当前铺在界面上的三张卡（AI 选卡按下标取）
    private Action<string> aiReply; // AI 开面板后等种子回来再回话

    public SelectCardView(UI_SelectCardPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.select_card;

    public bool IsOpen => IsVisible;

    // AI 查询用：某档还剩几次待抽
    public int AiPendingCount(int tier) => tier >= 0 && tier < pending.Length ? pending[tier] : 0;

    // AI 开面板：卡面要等服务器种子，回复也延迟到铺好那一刻
    public void OpenForAi(int tier, Action<string> reply)
    {
        if (tier < 0 || tier >= CardCatalog.TierCount)
        {
            reply("错误：档位只能是 0 白银 / 1 黄金 / 2 炫彩");
            return;
        }
        if (pending[tier] <= 0)
        {
            reply("错误：该档没有待抽次数");
            return;
        }
        aiReply = reply;
        OpenPanel((CardTier)tier);
    }

    // AI 选卡：下标就是界面上第几张
    public bool SelectForAi(int index, out string error)
    {
        if (!panel.IsOpen)
        {
            error = "抽卡界面没有打开";
            return false;
        }
        if (index < 0 || index >= currentRolls.Count)
        {
            error = currentRolls.Count == 0 ? "卡面还没铺开" : $"卡面下标只能是 0~{currentRolls.Count - 1}";
            return false;
        }
        error = null;
        OnCardSelected(index);
        return true;
    }

    private void ReplyToAi(string text)
    {
        var reply = aiReply;
        aiReply = null;
        reply?.Invoke(text);
    }

    private static string CardText(List<CardRoll> rolls, CardTier tier, int luck)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("已开卡 档位=").Append(tier).Append(" 幸运=").Append(luck);
        for (int i = 0; i < rolls.Count; i++)
        {
            builder.Append('\n').Append(i).Append(": ").Append(rolls[i].title);
            if (rolls[i].IsSkill) builder.Append("（技能 ").Append(rolls[i].skillId).Append('）');
        }
        return builder.ToString();
    }

    protected override void Init()
    {
        panel.Build(OnCardSelected, OnCardDiscarded, OnCardPanelClosed); // 关闭 = 不发包，种子留给下次
        panel.CloseImmediately();

        EventManager.AddEvent<SCPlayerProgress>(ClientEvent.OnPlayerProgress, OnPlayerProgress);
        EventManager.AddEvent<SCCardSeed>(ClientEvent.OnCardSeed, OnCardSeed);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleReset);
        EventManager.AddEvent(ClientEvent.OnRestartGame, ResetAll);
        EventManager.AddEvent(ClientEvent.OnExitWorld, ResetAll);
        RefreshHud();
    }

    public override void Shutdown()
    {
        EventManager.RemoveEvent<SCPlayerProgress>(ClientEvent.OnPlayerProgress, OnPlayerProgress);
        EventManager.RemoveEvent<SCCardSeed>(ClientEvent.OnCardSeed, OnCardSeed);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleReset);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, ResetAll);
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, ResetAll);
    }

    public override void Exit()
    {
        panel.Close();
        base.Exit();
    }

    // 选中后收面板还是就地等下一组，都在这里决定
    private void OnCardSelected(int index)
    {
        int tier = (int)currentTier;
        if (pending[tier] > 0) pending[tier]--;
        RefreshHud();

        bool more = pending[tier] > 0;
        awaitingSeed = more;
        SendCardAction((CardTier)tier, hasChoice: true, choice: (byte)index, requestSeed: more);
        if (more) panel.WaitForSeed((CardTier)tier); // 先隐掉卡面，等下一个种子
        else Close();
    }

    private void OnCardDiscarded()
    {
        int tier = (int)currentTier;
        if (pending[tier] > 0) pending[tier]--;
        RefreshHud();

        bool more = pending[tier] > 0;
        awaitingSeed = more;
        SendCardAction((CardTier)tier, hasChoice: true, choice: CSCardAction.DiscardChoice, requestSeed: more);
        if (more) panel.WaitForSeed((CardTier)tier);
        else Close();
    }

    // 手动关闭：不发包，服务器保留没用掉的种子（下次点开原样下发）
    private void OnCardPanelClosed()
    {
        awaitingSeed = false;
        Close();
        ReplyToAi("错误：抽卡界面已关闭（种子留着，下次打开还是这组）");
    }

    // 服务器只下发「每档有多少次」，次数变化即重画悬浮按钮
    private void OnPlayerProgress(SCPlayerProgress info)
    {
        pending[0] = info.silver;
        pending[1] = info.gold;
        pending[2] = info.holo;

        // 面板开着但这一档已经被抽完（例如弃卡后服务器结算）→ 收起，避免显示过期卡面
        if (panel.IsOpen && pending[(int)panel.OpenedTier] <= 0)
        {
            Close();
            ReplyToAi("错误：该档次数已用完");
        }

        RefreshHud();
    }

    // 种子到了才铺卡：幸运也只在种子里，客户端不再单独存
    private void OnCardSeed(SCCardSeed info)
    {
        if (!awaitingSeed || !panel.IsOpen) return; // 等的时候界面已经收起就不理会，服务器留着这个种子
        awaitingSeed = false;

        var tier = (CardTier)info.tier;
        if (!info.valid)
        {
            Close();
            ShowNoCard();
            ReplyToAi("错误：该档暂无可抽的卡");
            return;
        }
        ShowCardSet(tier, info.seed, info.luck);
    }

    // 用种子抽一组卡铺进界面：入口与「选完还有次数」共用
    private void ShowCardSet(CardTier tier, int seed, int luck)
    {
        var held = Skills != null ? new List<int>(Skills.HeldSkills) : new List<int>();
        var rolls = CardCatalog.Roll(tier, luck, CardCampForRoll(), CardCatalog.RollCount, held, seed);
        if (rolls.Count == 0)
        {
            Close();
            ShowNoCard();
            ReplyToAi("错误：该档暂无可抽的卡");
            return;
        }
        currentTier = tier;
        currentRolls.Clear();
        currentRolls.AddRange(rolls);
        panel.Open(rolls, tier, luck);
        if (Tool.UIManager != null) Tool.UIManager.ShowPage(PageKey);
        ReplyToAi(CardText(rolls, tier, luck));
    }

    // 点悬浮按钮：面板先进等待态，种子回来才铺卡
    private void OpenPanel(CardTier tier)
    {
        if (pending[(int)tier] <= 0 || Tool.UIManager == null) return;
        currentTier = tier;
        panel.WaitForSeed(tier);
        awaitingSeed = true;
        Tool.UIManager.ShowPage(PageKey);
        SendCardAction(tier, hasChoice: false, choice: 0, requestSeed: true);
    }

    private void Close()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(PageKey);
    }

    private static void ShowNoCard()
    {
        if (Tool.UIManager != null) Tool.UIManager.ShowFlyText(promptEnum.no_card_in_tier);
    }

    private void SendCardAction(CardTier tier, bool hasChoice, byte choice, bool requestSeed)
    {
        if (Tool.NetworkManager == null) return;
        Tool.NetworkManager.SendCardAction(new CSCardAction
        {
            tier = (byte)tier,
            hasChoice = hasChoice,
            choice = choice,
            requestSeed = requestSeed,
        });
    }

    // card_option.csv 的阵营限制：0 = 进攻方，1 = 防守方
    private int CardCampForRoll()
    {
        var battle = Battle;
        return battle != null && battle.MyCamp == EntityCamp.Defense ? 1 : 0;
    }

    // 悬浮按钮：只放仍有次数的档位
    private void RefreshHud()
    {
        var list = HudList;
        if (list == null) return;
        pendingTiers.Clear();
        for (int tier = 0; tier < pending.Length; tier++)
            if (pending[tier] > 0) pendingTiers.Add(tier);
        list.itemRenderer = RenderCardButton;
        list.numItems = pendingTiers.Count;
    }

    private void RenderCardButton(int index, GObject obj)
    {
        if (obj is not UI_btn_SelectCard btn || index >= pendingTiers.Count) return;
        int tier = pendingTiers[index];
        if (btn.m_level != null) btn.m_level.selectedIndex = tier;
        if (btn.m_label_num != null) btn.m_label_num.text = pending[tier].ToString();
        btn.onClick.Set(() => OpenPanel((CardTier)tier));
    }

    private void OnBattleReset(int camp) => ResetAll();

    private void ResetAll()
    {
        for (int i = 0; i < pending.Length; i++) pending[i] = 0;
        pendingTiers.Clear();
        currentRolls.Clear();
        currentTier = CardTier.Silver;
        awaitingSeed = false;
        if (Tool.UIManager != null) Tool.UIManager.HidePage(PageKey);
        RefreshHud();
        ReplyToAi("错误：抽卡状态已重置");
    }

    private static ClientSkillManager Skills => Tool.ClientLogicManager != null ? Tool.ClientLogicManager.SkillVfx : null;

    private static BattlePage Battle => Tool.UIManager != null ? Tool.UIManager.Battle : null;

    private static GList HudList => Battle != null ? Battle.CardButtonList : null;
}
