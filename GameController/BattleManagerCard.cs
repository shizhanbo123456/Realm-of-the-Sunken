using System.Collections.Generic;
using UnityEngine;

// 玩家级词条：记录每名玩家的词条累计等级，并派生出聚合加成；建筑类词条按阵营累加后供己方建筑读取
public partial class BattleManager
{
    private readonly Dictionary<int, Dictionary<string, int>> cardLevels = new();
    private readonly Dictionary<int, CardBonus> playerCardBonus = new();
    private readonly Dictionary<EntityCamp, CardBonus> campCardBonus = new();

    // 抽到一张卡：同类型等级累加（力量 I 之后再拿力量 II 就是 3 级）
    public void AddCardLevel(int clientId, string type, int level)
    {
        if (!cardLevels.TryGetValue(clientId, out var levels))
        {
            levels = new Dictionary<string, int>();
            cardLevels[clientId] = levels;
        }
        levels.TryGetValue(type, out int current);
        levels[type] = current + level;
        RebuildPlayerCardBonus(clientId);
    }

    public int GetCardLevel(int clientId, string type)
    {
        return cardLevels.TryGetValue(clientId, out var levels) && levels.TryGetValue(type, out int level) ? level : 0;
    }

    // 实体所属玩家的词条；非玩家实体返回空对象（永不修改）
    public CardBonus GetEntityCardBonus(EntityData entity)
    {
        if (entity == null) return CardBonus.Empty;
        if (!EntityOwnerClient.TryGetValue(entity.id, out int clientId)) return CardBonus.Empty;
        return playerCardBonus.TryGetValue(clientId, out var bonus) ? bonus : CardBonus.Empty;
    }

    public CardBonus GetPlayerCardBonus(int clientId)
    {
        return playerCardBonus.TryGetValue(clientId, out var bonus) ? bonus : CardBonus.Empty;
    }

    // 阵营词条 = 该阵营所有玩家累加（多名防守方的塔攻击/守护点减伤按份数叠加）
    public CardBonus GetCampCardBonus(EntityCamp camp)
    {
        if (campCardBonus.TryGetValue(camp, out var cached)) return cached;
        var sum = new CardBonus();
        foreach (var pair in PlayerCamps)
        {
            if (ToEntityCamp(pair.Value) != camp) continue;
            if (!playerCardBonus.TryGetValue(pair.Key, out var bonus)) continue;
            sum.AddInto(bonus);
        }
        campCardBonus[camp] = sum;
        return sum;
    }

    // 出伤乘区：对玩家/珊瑚/建筑增伤、斗士、反击、等级压制（每级 ±5%，等级差夹 3 级）
    // 等级压制写成一条式即可覆盖双方：高等级打低等级增伤，低等级打高等级减伤
    public float GetOutgoingDamageMultiplier(DamageStat stat, EntityData target)
    {
        float mult = 1f;
        var card = stat.card ?? CardBonus.Empty;
        if (target != null)
        {
            switch (target.type.category)
            {
                case EntityCategory.Character_Attack:
                case EntityCategory.Character_Defense:
                    mult += card.damageVsPlayer;
                    break;
                case EntityCategory.Coral:
                    mult += card.damageVsCoral;
                    break;
                case EntityCategory.CrystalStatue:
                case EntityCategory.GuardStatue:
                    mult += card.damageVsBuilding;
                    break;
            }

            if (card.damageVsHigherHp > 0f && target.attributes != null && stat.healthRatio > 0f
                && target.attributes.Health / target.attributes.MaxHealth > stat.healthRatio)
                mult += card.damageVsHigherHp;

            int diff = Mathf.Clamp(stat.level - target.level, -Config.level_suppress_max_diff, Config.level_suppress_max_diff);
            mult += Config.level_suppress_per_level * diff;
        }

        if (card.damageBelowHalfHp > 0f && stat.healthRatio < 0.5f) mult += card.damageBelowHalfHp;
        return Mathf.Max(0f, mult);
    }

    private void RebuildPlayerCardBonus(int clientId)
    {
        if (!playerCardBonus.TryGetValue(clientId, out var bonus))
        {
            bonus = new CardBonus();
            playerCardBonus[clientId] = bonus;
        }
        bonus.Clear();
        foreach (var pair in cardLevels[clientId]) bonus.Add(pair.Key, pair.Value);
        campCardBonus.Clear(); // 阵营聚合依赖个人聚合，一并失效
    }

    // 对局结束清空：词条是玩家级数据，跨局不保留
    private void ResetCards()
    {
        cardLevels.Clear();
        playerCardBonus.Clear();
        campCardBonus.Clear();
    }

    private static EntityCamp ToEntityCamp(PlayerCampType camp)
    {
        return camp switch
        {
            PlayerCampType.Attack => EntityCamp.Attack,
            PlayerCampType.Defense => EntityCamp.Defense,
            _ => EntityCamp.Neutral,
        };
    }
}
