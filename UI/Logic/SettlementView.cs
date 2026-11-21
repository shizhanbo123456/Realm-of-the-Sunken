using System.Collections.Generic;
using FairyGUI;
using Ros.UI.Main;
using UnityEngine;

using Ros.Config;
// 结算界面：独立的全局子界面，不挂在任何页面上（挂 GRoot）。
// 战斗结束走 OnSettlementResult 显示；GM「结算界面」演示直接调 PlayDemo。
public class SettlementView
{
    private const float DetailStartDelay = 0.5f;   // 面板转场后首条细节出现
    private const float DetailItemInterval = 0.3f; // 相邻两条细节的间隔
    private const float FadeDuration = 0.5f;       // 点关闭后整个面板的淡出时长

    private UI_SettlementPanel panel;
    private readonly List<UI_BattleResultDetailItem> items = new();
    private readonly List<string> rows = new();
    private Timer.TransitionHandle transition;
    private int pendingAppear; // 还没播完出现动画的明细条数
    private int showSerial;    // 每次显示递增，用来丢弃上一次面板残留的动画回调
    private bool canClose;     // 出现动画全部播完才允许关闭
    private bool closing;      // 淡出中，阻断再次点击
    private bool restoreLobby; // 战斗结束流程：关闭时恢复组队面板

    public void Construct()
    {
        EventManager.AddEvent<SettlementResult>(ClientEvent.OnSettlementResult, OnSettlementResult);
    }

    public void Shutdown()
    {
        EventManager.RemoveEvent<SettlementResult>(ClientEvent.OnSettlementResult, OnSettlementResult);
        CancelAnimations();
    }

    // GM 演示：本地填演示数据 + 播放结算动画；不切页、不动战斗实体
    public void PlayDemo()
    {
        var demo = new SettlementResult
        {
            gameState = 1, // 进攻方胜
            attackScore = 1280f,
            defenseScore = 960f,
            killScore = 8,
            remainTime = 120f,
            expGain = 800,
            guardStatueHealth = 40f,
        };
        Show(demo, EntityCamp.Attack, false);
    }

    #region//Local
    private void OnSettlementResult(SettlementResult r)
    {
        // 先清战斗表现，防过时内容残留
        if (Tool.ClientLogicManager != null) Tool.ClientLogicManager.EntityPlayers.ClearAll();

        var loading = ClientLoadingManager.Current;
        EntityCamp camp = EntityCamp.None;
        if (loading != null && loading.HasSession) camp = loading.Camp;
        Show(r, camp, true);

        // 战斗页退出（HUD 随 Exit 隐藏），组队页留到结算关闭时再恢复
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.battle);
    }

    private void Show(SettlementResult r, EntityCamp camp, bool restoreLobbyAfterClose)
    {
        restoreLobby = restoreLobbyAfterClose;
        if (panel == null)
        {
            panel = UI_SettlementPanel.CreateInstance();
            GRoot.inst.AddChild(panel); // 挂全局层：结算要跨页面盖在组队界面之上
            panel.visible = false;
        }
        // 铺满屏幕：按 1080 高的逻辑尺寸摆放并整体缩放，标题与明细的居中交给面板内的对齐关系
        float uiScale = Screen.height / 1080f;
        panel.scale = new Vector2(uiScale, uiScale);
        panel.SetSize(1080f * ((float)Screen.width / Screen.height), 1080f);
        panel.SetXY(0f, 0f);

        var result = panel.m_battle_result;
        result.m_title.text = GetEndText(r.gameState, camp);
        result.m_content.text = MessageVariable.F(message_variablesEnum.settlement_battleTime,
            Mathf.RoundToInt(Mathf.Max(0f, GlobalVariable.F(global_variablesEnum.battle_duration) - r.remainTime)));

        BuildRows(r, camp);

        panel.visible = true;
        panel.alpha = 1f; // 每次显示从完全不透明开始
        result.m_t0.Play();
        panel.m_close.onClick.Set(OnCloseClick);
        canClose = false;
        closing = false;
        pendingAppear = items.Count;
        if (pendingAppear == 0) canClose = true; // 没有明细就没有待播的出现动画
        ScheduleAppear(++showSerial);
    }

    // 关闭按钮：出现动画没播完、或正在淡出时一律不响应
    private void OnCloseClick()
    {
        if (!canClose || closing || panel == null) return;
        closing = true;
        FadeOut();
    }

    // 整体淡出：压面板 alpha，按钮随面板一起淡出；走完即销毁
    private void FadeOut()
    {
        CancelAnimations();
        transition = Timer.AddTransition(0, FadeDuration, (_, t01) =>
        {
            if (t01 >= 1f)
            {
                transition = null; // 自身已结束，避免在回调内 Cancel 自身
                Close();
                return;
            }
            if (panel != null) panel.alpha = 1f - t01;
        });
    }

    private void BuildRows(SettlementResult r, EntityCamp camp)
    {
        rows.Clear();
        items.Clear();
        // 分数只报本地阵营；观战方没有自己的分数，攻防拆成两条
        if (camp == EntityCamp.Attack) rows.Add(MessageVariable.F(message_variablesEnum.settlement_score, (int)r.attackScore));
        else if (camp == EntityCamp.Defense) rows.Add(MessageVariable.F(message_variablesEnum.settlement_score, (int)r.defenseScore));
        else
        {
            rows.Add(MessageVariable.F(message_variablesEnum.settlement_attackScore, (int)r.attackScore));
            rows.Add(MessageVariable.F(message_variablesEnum.settlement_defenseScore, (int)r.defenseScore));
        }
        rows.Add(MessageVariable.F(message_variablesEnum.settlement_kill, r.killScore));
        if (camp == EntityCamp.Attack)
            rows.Add(MessageVariable.F(message_variablesEnum.settlement_guardDamage, (int)r.expGain));
        else
            rows.Add(MessageVariable.F(message_variablesEnum.settlement_guardHealth, (int)r.guardStatueHealth));

        var list = panel.m_battle_result_detail.m_resultList;
        list.itemRenderer = RenderRow;
        list.numItems = rows.Count; // 非虚拟列表同步建条目并回调渲染器
    }

    private void RenderRow(int index, GObject obj)
    {
        if (obj is not UI_BattleResultDetailItem item) return;
        item.alpha = 0f; // 出现前整体隐藏，到点播放 t0 时解开
        if (index < rows.Count) item.m_content.text = rows[index];
        else item.m_content.text = string.Empty;
        if (!items.Contains(item)) items.Add(item); // 动画序列按此顺序逐条播放
    }

    // 按间隔逐条放出明细的出现动画，最后一条播完才放开关闭
    private void ScheduleAppear(int serial)
    {
        CancelAnimations();
        if (items.Count == 0) return;
        int shown = 0;
        float span = DetailStartDelay + (items.Count - 1) * DetailItemInterval;
        transition = Timer.AddTransition(0, span, (_, t01) =>
        {
            float elapsed = t01 * span;
            // 到达出现时刻的条目逐条播放自身动画（shown 指针保证每条只播一次）
            while (shown < items.Count && elapsed >= DetailStartDelay + shown * DetailItemInterval)
            {
                var item = items[shown++];
                if (item == null || item.displayObject == null)
                {
                    OnItemShown(serial);
                    continue;
                }
                item.alpha = 1f; // 显示期间整体保持不透明，淡入由 t0 的 Alpha 通道完成
                item.m_t0.Play(() => OnItemShown(serial));
            }
            if (t01 >= 1f) transition = null; // 自身已结束，避免在回调内 Cancel 自身
        });
    }

    // 单条明细的出现动画播完：全部播完才接受关闭点击；上一次面板的残留回调直接丢弃
    private void OnItemShown(int serial)
    {
        if (serial != showSerial) return;
        if (pendingAppear > 0) pendingAppear--;
        if (pendingAppear == 0) canClose = true;
    }

    private void CancelAnimations()
    {
        if (transition != null) transition.Cancel();
        transition = null;
    }

    // 淡出结束：销毁面板（标题、明细与关闭按钮都是面板子对象，随面板一起销毁）；战斗结束流程再恢复组队面板
    private void Close()
    {
        CancelAnimations();
        if (panel != null)
        {
            panel.Dispose();
            panel = null;
        }
        showSerial++; // 让仍在飞的明细出现回调全部失效
        pendingAppear = 0;
        canClose = false;
        closing = false;
        items.Clear();
        rows.Clear();
        if (restoreLobby && Tool.UIManager != null) Tool.UIManager.ShowPage(pageEnum.lobby);
        restoreLobby = false;
    }

    private static string GetEndText(int gameState, EntityCamp camp)
    {
        bool win = (gameState == 1 && camp == EntityCamp.Attack)
                || (gameState == 2 && camp == EntityCamp.Defense);
        if (win) return MessageVariable.V(message_variablesEnum.settlement_win);
        return MessageVariable.V(message_variablesEnum.settlement_end);
    }
    #endregion
}
