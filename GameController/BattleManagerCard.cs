using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
// 玩家级词条与局内进度：词条等级累加后派生加成；等级/经验/幸运/待抽次数也挂在这里（玩家级，不在实体上）
// 建筑类词条按阵营累加后供己方建筑读取
public partial class BattleManager
{
    private readonly Dictionary<int, Dictionary<string, int>> cardLevels = new();
    private readonly Dictionary<int, CardBonus> playerCardBonus = new();
    private readonly Dictionary<EntityCamp, CardBonus> campCardBonus = new();

    // 局内进度：等级决定伤害压制与升级抽卡，exp 只记当前等级内的进度
    private class PlayerProgress
    {
        public int level = 1;
        public int exp;
        public readonly int[] pending = new int[CardCatalog.TierCount]; // 白银/黄金/炫彩 各自的待抽次数
    }

    private readonly Dictionary<int, PlayerProgress> playerProgress = new();
    private float guardTotalHealth; // 开战瞬间守护点总血量，经验增幅按「已扣除比例」算

    #region 词条
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

    // 出伤乘区：对玩家/僵尸/珊瑚/建筑增伤、斗士、反击、等级压制（每级 ±5%，等级差夹 3 级）
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
                case EntityCategory.Zombie:
                case EntityCategory.EliteZombie:
                    mult += card.damageVsZombie;
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

            int diff = Mathf.Clamp(stat.level - target.level, -GlobalConfig.level_suppress_max_diff, GlobalConfig.level_suppress_max_diff);
            mult += GlobalConfig.level_suppress_per_level * diff;
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

    // 进攻方各角色被动对应的词条类型（顺序 = 角色 value，与 passive_attack_layer 一一对应）
    private static readonly string[] AttackPassiveCardTypes =
    {
        CardType.DamageBelowHalfHp, // 瑟莱恩 绝地反击
        CardType.Luck,              // 布洛克 命运眷顾
        CardType.DamageVsPlayer,    // 摩洛克 恶魔侵蚀
        CardType.DamageVsZombie,    // 艾瑟琳 斩首斗术
        CardType.DamageVsBuilding,  // 霍里特 墟角秘术
        CardType.DamageVsHigherHp,  // 戈拉姆 战斗意志
    };

    // 角色被动折算成词条层数，层数取自 global_variables 的 passive_attack_layer
    // 玩家级数据，复活沿用；目前只有进攻方角色带被动词条
    private void ApplyCharacterCardLevels(int clientId, EntityType character)
    {
        if (character.category != EntityCategory.Character_Attack) return;
        if (character.value < 0 || character.value >= AttackPassiveCardTypes.Length) return; // 新增但未配被动的角色
        var layers = GlobalVariable.L(global_variablesEnum.passive_attack_layer);
        if (layers.Count != AttackPassiveCardTypes.Length)
            throw new System.FormatException($"[配表] passive_attack_layer 需要 {AttackPassiveCardTypes.Length} 个值，实际 {layers.Count}");
        SeedCardLevel(clientId, AttackPassiveCardTypes[character.value], Mathf.RoundToInt(layers[character.value]));
    }

    // 被动给的初始层数直接置位，与抽卡的累加语义分开，重复开战不会翻倍
    private void SeedCardLevel(int clientId, string type, int level)
    {
        if (!cardLevels.TryGetValue(clientId, out var levels))
        {
            levels = new Dictionary<string, int>();
            cardLevels[clientId] = levels;
        }
        levels[type] = level;
        RebuildPlayerCardBonus(clientId);
    }
    #endregion

    #region 等级与经验
    public int GetPlayerLevel(int clientId)
    {
        return playerProgress.TryGetValue(clientId, out var p) ? p.level : 1;
    }

    private PlayerProgress EnsureProgress(int clientId)
    {
        if (!playerProgress.TryGetValue(clientId, out var p))
        {
            p = new PlayerProgress();
            playerProgress[clientId] = p;
        }
        return p;
    }

    // 经验增幅：守护点扣得越多、时间越早，防守方拿得越多；反过来进攻方拿得越多
    // 公式按策划口径：(扣血比例% × 2 − 时间比例%) × 10 = 增幅基数%
    private float ExpMultiplier(int clientId)
    {
        float duration = GlobalVariable.F(global_variablesEnum.battle_duration);
        if (duration <= 0f) return 1f;

        float timePct = Mathf.Clamp01(1f - BattleRemainTime / duration) * 100f;
        float lostPct = GuardLostRatio() * 100f;
        float basePct = (lostPct * 2f - timePct) * 10f;

        if (!PlayerCamps.TryGetValue(clientId, out var camp)) return 1f;
        if (camp == PlayerCampType.Defense)
            return Mathf.Max(0.3f, 1f + basePct / 100f);

        int attackCount = 0;
        foreach (var pair in PlayerCamps)
        {
            if (pair.Value == PlayerCampType.Attack) attackCount++;
        }
        if (attackCount <= 0) return 1f;
        return Mathf.Max(0.3f, (1f - basePct / 100f) * 4f / attackCount);
    }

    private float GuardLostRatio()
    {
        if (guardTotalHealth <= 0f) return 0f;
        return Mathf.Clamp01(1f - StatueRemainingHealth() / guardTotalHealth);
    }

    // 开战建档：守护点总血量只在这一次记录，之后靠「剩余血量」反推已扣除比例
    private void CaptureGuardTotalHealth()
    {
        guardTotalHealth = 0f;
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue != null) guardTotalHealth += guardStatue.attributes.MaxHealth;
        }
    }

    // 击倒归属方加经验；applyMultiplier = false 用于弃卡这类直接折算，不吃增幅
    public void GainExp(int clientId, int amount, bool applyMultiplier = true)
    {
        if (amount <= 0) return;
        var p = EnsureProgress(clientId);
        if (p.level >= CharacterExpConfig.MaxLevel) return; // 满级不再吃经验

        int final = applyMultiplier ? Mathf.RoundToInt(amount * ExpMultiplier(clientId)) : amount;
        if (final <= 0) return;
        p.exp += final;
        LevelUp(clientId, p);
        SendProgress(clientId);
    }

    private void LevelUp(int clientId, PlayerProgress p)
    {
        while (p.level < CharacterExpConfig.MaxLevel)
        {
            int need = CharacterExpConfig.NeedExp(p.level + 1);
            if (need <= 0 || p.exp < need) break;
            p.exp -= need;
            p.level++;

            if (TryGetPlayerEntity(clientId, out var entity)) entity.level = p.level; // 压制按实体等级算

            int[] weights = CardGainUpgradeConfig.WeightsOf(p.level); // 按升级后等级的个位数给档位
            int tier = CardCatalog.RollTier(weights);
            if (tier >= 0) p.pending[tier]++;
        }
        // 满级后余经验不再累积，避免读数永远停在 MAX 之前
        if (p.level >= CharacterExpConfig.MaxLevel) p.exp = 0;
    }

    private bool TryGetPlayerEntity(int clientId, out EntityData entity)
    {
        entity = null;
        if (!PlayerEntityId.TryGetValue(clientId, out var entityId)) return false;
        return EntityContainer.Entities.TryGetObject(entityId, out entity) && entity != null;
    }
    #endregion

    #region 掉落
    // 任意实体被击败：按 entity.csv 的掉落经验与卡牌权重给归属方（范围列决定只给击杀者还是全阵营）
    private void GrantKillRewards(EntityData victim)
    {
        if (victim == null) return;
        var killer = victim.lastAttacker;
        if (killer == null) return;

        var config = EntityConfig.GetByCategory(victim.type);
        bool hasCard = config.cardDropWeights != null && (config.cardDropWeights[0] > 0 || config.cardDropWeights[1] > 0 || config.cardDropWeights[2] > 0);
        if (config.expDrop <= 0 && !hasCard) return;

        s_rewardClients.Clear();
        if (config.dropToCamp)
        {
            foreach (var pair in PlayerCamps)
            {
                if (ToEntityCamp(pair.Value) == killer.camp) s_rewardClients.Add(pair.Key);
            }
        }
        else if (EntityOwnerClient.TryGetValue(killer.id, out int killerClient) && PlayerCamps.ContainsKey(killerClient))
        {
            s_rewardClients.Add(killerClient);
        }

        foreach (int clientId in s_rewardClients)
        {
            if (config.expDrop > 0) GainExp(clientId, config.expDrop);

            int tier = CardCatalog.RollTier(config.cardDropWeights);
            if (tier < 0) continue;
            EnsureProgress(clientId).pending[tier]++;
            SendProgress(clientId);
        }
    }

    private static readonly List<int> s_rewardClients = new();
    #endregion

    #region 卡牌结算
    // 选卡：消耗该档一次待抽次数，按类型立刻生效
    public void ApplyCard(int clientId, int tier, int cardId, int skillId)
    {
        if (tier < 0 || tier >= CardCatalog.TierCount) return;
        var p = EnsureProgress(clientId);
        if (p.pending[tier] <= 0) return;
        if (!CardOptionConfig.content.TryGetValue(cardId, out var card)) return;
        // 技能卡必须带具体技能 id，否则这一抽作废（不扣次数）
        if (IsSkillType(card.type) && skillId < 0) return;

        p.pending[tier]--;

        switch (card.type)
        {
            case CardType.SkillB:
            case CardType.SkillA:
            case CardType.SkillS:
                GrantSkill(clientId, skillId);
                break;
            case CardType.InstantHeal:
                HealByPercent(clientId, GlobalVariable.F(global_variablesEnum.card_coef_instant_heal_boost) * card.value / 100f);
                break;
            default:
                AddCardLevel(clientId, card.type, card.value);
                break;
        }
        SendProgress(clientId);
    }

    private static bool IsSkillType(string type)
    {
        return type == CardType.SkillB || type == CardType.SkillA || type == CardType.SkillS;
    }

    // 弃卡：整组折算成经验（不吃经验增幅，避免"抽了就弃"滚雪球）
    public void DiscardCard(int clientId, int tier)
    {
        if (tier < 0 || tier >= CardCatalog.TierCount) return;
        var p = EnsureProgress(clientId);
        if (p.pending[tier] <= 0) return;

        p.pending[tier]--;
        int exp = tier switch
        {
            0 => GlobalVariable.I(global_variablesEnum.card_discard_exp_silver),
            1 => GlobalVariable.I(global_variablesEnum.card_discard_exp_gold),
            _ => GlobalVariable.I(global_variablesEnum.card_discard_exp_holo),
        };
        GainExp(clientId, exp, applyMultiplier: false);
        SendProgress(clientId);
    }

    // 技能卡：服务器只记持有，进不进技能栏由客户端决定
    private void GrantSkill(int clientId, int skillId)
    {
        if (!TryGetPlayerEntity(clientId, out var player) || player.skillController == null) return;
        if (skillId < 0) return;
        if (player.skillController.GetSkillIds().Contains(skillId)) return;

        player.skillController.AddSkill(skillId);
        var sync = new Ros.Transport.SCSkillSync { full = false };
        sync.skills.Add(skillId);
        Tool.NetworkManager.SendSkillSync(clientId, sync);
        SendSkillRecharge(clientId); // 新技能可能自动进栏
        NotifyPlayer(clientId, 14);
    }

    private void HealByPercent(int clientId, float percent)
    {
        if (percent <= 0f) return;
        if (!TryGetPlayerEntity(clientId, out var player) || !player.Alive) return;
        player.attributes.Heal(player.attributes.MaxHealth * percent);
        player.healthDirty = true;
    }
    #endregion

    #region 同步
    private void SendProgress(int clientId)
    {
        if (Tool.NetworkManager == null) return;
        var p = EnsureProgress(clientId);
        Tool.NetworkManager.SendPlayerProgress(clientId, new Ros.Transport.SCPlayerProgress
        {
            level = p.level,
            exp = p.exp,
            luck = GetPlayerCardBonus(clientId).luck,
            silver = p.pending[0],
            gold = p.pending[1],
            holo = p.pending[2],
        });
    }

    // 开战/重连补发：只要还没进过战斗也要能看到自己的进度
    private void SendProgressToAll()
    {
        foreach (var pair in PlayerCamps) SendProgress(pair.Key);
    }
    #endregion

    // 对局结束清空：词条与局内进度都是玩家级数据，跨局不保留
    private void ResetCards()
    {
        cardLevels.Clear();
        playerCardBonus.Clear();
        campCardBonus.Clear();
        playerProgress.Clear();
        guardTotalHealth = 0f;
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
