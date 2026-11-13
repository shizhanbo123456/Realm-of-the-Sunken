using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
public enum CardTier
{
    Silver = 0,
    Gold = 1,
    Holo = 2,
}

// 一次抽到的一张卡：非技能卡 skillId = -1，技能卡带具体技能 id（显示名与图标都用它）
public struct CardRoll
{
    public int cardId;
    public CardTier tier;
    public string type;
    public int value;
    public int skillId;
    public string title;
    public string description;

    public bool IsSkill => skillId >= 0;
}

// 抽卡规则：档位权重由掉落来源决定（entity.csv / card_gain_upgrade.csv），
// 档位内的候选由「幸运区间 + 阵营限制」筛出 card_option.csv 的行，再按权重抽不重复的若干张。
// 客户端抽、服务器结算：服务器只下发待抽次数，抽到什么由这里算
public static class CardCatalog
{
    public const int TierCount = 3;

    // 按权重滚一档；全 0 = 不掉卡，返回 -1
    public static int RollTier(IReadOnlyList<int> weights)
    {
        if (weights == null || weights.Count < TierCount) return -1;
        int total = 0;
        for (int i = 0; i < TierCount; i++)
        {
            if (weights[i] < 0) return -1;
            total += weights[i];
        }
        if (total <= 0) return -1;

        int roll = Random.Range(0, total);
        for (int i = 0; i < TierCount; i++)
        {
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return TierCount - 1;
    }

    // 抽 count 张互不重复的卡；候选不足时按实际数量返回
    public static List<CardRoll> Roll(CardTier tier, int luck, int camp, int count, ICollection<int> heldSkills)
    {
        var result = new List<CardRoll>(count);
        var candidateIds = new List<int>();
        var candidateWeights = new List<int>();

        foreach (var pair in CardOptionConfig.content)
        {
            var card = pair.Value;
            if (!card.RangeOf((int)tier).Contains(luck)) continue;
            if (card.camp >= 0 && card.camp != camp) continue;
            if (IsSkillType(card.type) && !HasSkillOfQuality(QualityOf(card.type), heldSkills)) continue;
            candidateIds.Add(card.key);
            candidateWeights.Add(card.weight);
        }

        while (result.Count < count && candidateIds.Count > 0)
        {
            int index = PickWeighted(candidateWeights);
            int cardId = candidateIds[index];
            candidateIds.RemoveAt(index);
            candidateWeights.RemoveAt(index);
            result.Add(Build(cardId, tier, heldSkills));
        }
        return result;
    }

    // 池子里的技能 id（武器段内该品质、不含已持有）
    public static List<int> SkillPool(string quality, ICollection<int> heldSkills)
    {
        var pool = new List<int>();
        for (int id = GlobalConfig.weapon_skill_id_min; id <= GlobalConfig.weapon_skill_id_max; id++)
        {
            if (heldSkills != null && heldSkills.Contains(id)) continue;
            if (!SkillConfig.content.TryGetValue(id, out var skill)) continue;
            if (skill.quality != quality) continue;
            pool.Add(id);
        }
        return pool;
    }

    private static CardRoll Build(int cardId, CardTier tier, ICollection<int> heldSkills)
    {
        var card = CardOptionConfig.Get(cardId);
        var roll = new CardRoll
        {
            cardId = cardId,
            tier = tier,
            type = card.type,
            value = card.value,
            skillId = -1,
            title = card.title,
            description = card.description,
        };
        if (!IsSkillType(card.type)) return roll;

        var pool = SkillPool(QualityOf(card.type), heldSkills);
        roll.skillId = pool[Random.Range(0, pool.Count)];
        roll.description = card.description.Replace("{0}", SkillConfig.Get(roll.skillId).name);
        return roll;
    }

    private static int PickWeighted(List<int> weights)
    {
        int total = 0;
        for (int i = 0; i < weights.Count; i++) total += weights[i];
        int roll = Random.Range(0, total);
        for (int i = 0; i < weights.Count; i++)
        {
            if (roll < weights[i]) return i;
            roll -= weights[i];
        }
        return weights.Count - 1;
    }

    private static bool IsSkillType(string type)
    {
        return type == CardType.SkillB || type == CardType.SkillA || type == CardType.SkillS;
    }

    private static string QualityOf(string type)
    {
        if (type == CardType.SkillB) return "B";
        if (type == CardType.SkillA) return "A";
        return "S";
    }

    private static bool HasSkillOfQuality(string quality, ICollection<int> heldSkills)
    {
        for (int id = GlobalConfig.weapon_skill_id_min; id <= GlobalConfig.weapon_skill_id_max; id++)
        {
            if (heldSkills != null && heldSkills.Contains(id)) continue;
            if (!SkillConfig.content.TryGetValue(id, out var skill)) continue;
            if (skill.quality == quality) return true;
        }
        return false;
    }
}
