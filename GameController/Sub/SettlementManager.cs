using Ros.Transport;

public struct SettlementResult
{
    public int gameState;
    public float attackScore;
    public float defenseScore;
    public int killScore;
    public float remainTime;
    public int expGain;
    public float guardStatueHealth;
}

public class SettlementManager : ClientSubManager
{
    private bool settled;

    protected override void BindEvents()
    {
        EventManager.AddEvent<SCScoreInfo>(ClientEvent.OnScoreUpdate, OnScoreUpdate);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
    }

    protected override void UnbindEvents()
    {
        EventManager.RemoveEvent<SCScoreInfo>(ClientEvent.OnScoreUpdate, OnScoreUpdate);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStart);
    }

    private void OnBattleStart(int camp) => settled = false;

    private void OnScoreUpdate(SCScoreInfo info)
    {
        if (info.gameState == 0 || settled) return;
        settled = true;
        var result = BuildSettlementResult(info);
        EventManager.TrigEvent(ClientEvent.OnBattleEnd, info.gameState);
        EventManager.TrigEvent(ClientEvent.OnSettlementResult, result);
    }

    #region//Local
    private SettlementResult BuildSettlementResult(SCScoreInfo info)
    {
        return new SettlementResult
        {
            gameState = info.gameState,
            attackScore = info.attackScore,
            defenseScore = info.defenseScore,
            killScore = info.killScore,
            remainTime = info.remainTime,
            expGain = info.expGain,
            guardStatueHealth = info.guardStatueHealth,
        };
    }
    #endregion
}
