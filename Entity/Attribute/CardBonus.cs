using UnityEngine;

// 卡牌词条类型键（与 card_option.csv「类型（代码用）」列一一对应）
public static class CardType
{
    public const string Luck = "luck_boost";
    public const string Health = "health_boost";
    public const string Attack = "attack_boost";
    public const string CritRate = "crit_rate_boost";
    public const string CritDamage = "crit_damage_boost";
    public const string DamageVsPlayer = "damage_vs_player";
    public const string DamageVsCoral = "damage_vs_coral";
    public const string DamageVsBuilding = "damage_vs_building";
    public const string TowerAttack = "tower_attack_boost";
    public const string GuardReduction = "guard_reduction_boost";
    public const string InstantHeal = "instant_heal_boost";
    public const string DamageBelowHalfHp = "damage_below_half_hp";
    public const string DamageVsHigherHp = "damage_vs_higher_hp";
    public const string SkillB = "skill_b";
    public const string SkillA = "skill_a";
    public const string SkillS = "skill_s";
}

// 一名玩家的词条聚合：等级累加后按 global_variables 的每级系数折算成实际加成
// 百分比字段一律存小数（0.05 = 5%），基准是角色基础属性
public class CardBonus
{
    public static readonly CardBonus Empty = new();

    public int luck;
    public float healthPercent;
    public int attackFlat;
    public int critRate;
    public int critDamage;
    public float damageVsPlayer;
    public float damageVsCoral;
    public float damageVsBuilding;
    public int towerAttack;
    public float guardReduction;
    public float damageBelowHalfHp;
    public float damageVsHigherHp;
    public int healPercent; // 一次性回复，抽到即结算，不进属性

    public void Clear()
    {
        luck = 0;
        healthPercent = 0f;
        attackFlat = 0;
        critRate = 0;
        critDamage = 0;
        damageVsPlayer = 0f;
        damageVsCoral = 0f;
        damageVsBuilding = 0f;
        towerAttack = 0;
        guardReduction = 0f;
        damageBelowHalfHp = 0f;
        damageVsHigherHp = 0f;
        healPercent = 0;
    }

    // 按类型累加 level 级；技能类词条只影响持有技能，不进属性
    public void Add(string type, int level)
    {
        if (level <= 0) return;
        switch (type)
        {
            case CardType.Luck: luck += ConfigManager.GlobalVariableConfig.card_coef_luck_boost * level; break;
            case CardType.Health: healthPercent += ConfigManager.GlobalVariableConfig.card_coef_health_boost * level / 100f; break;
            case CardType.Attack: attackFlat += ConfigManager.GlobalVariableConfig.card_coef_attack_boost * level; break;
            case CardType.CritRate: critRate += ConfigManager.GlobalVariableConfig.card_coef_crit_rate_boost * level; break;
            case CardType.CritDamage: critDamage += ConfigManager.GlobalVariableConfig.card_coef_crit_damage_boost * level; break;
            case CardType.DamageVsPlayer: damageVsPlayer += ConfigManager.GlobalVariableConfig.card_coef_damage_vs_player * level / 100f; break;
            case CardType.DamageVsCoral: damageVsCoral += ConfigManager.GlobalVariableConfig.card_coef_damage_vs_coral * level / 100f; break;
            case CardType.DamageVsBuilding: damageVsBuilding += ConfigManager.GlobalVariableConfig.card_coef_damage_vs_building * level / 100f; break;
            case CardType.TowerAttack: towerAttack += ConfigManager.GlobalVariableConfig.card_coef_tower_attack_boost * level; break;
            case CardType.GuardReduction: guardReduction += ConfigManager.GlobalVariableConfig.card_coef_guard_reduction_boost * level / 100f; break;
            case CardType.InstantHeal: healPercent += Mathf.RoundToInt(ConfigManager.GlobalVariableConfig.card_coef_instant_heal_boost * level); break;
            case CardType.DamageBelowHalfHp: damageBelowHalfHp += ConfigManager.GlobalVariableConfig.card_coef_damage_below_half_hp * level / 100f; break;
            case CardType.DamageVsHigherHp: damageVsHigherHp += ConfigManager.GlobalVariableConfig.card_coef_damage_vs_higher_hp * level / 100f; break;
            case CardType.SkillB:
            case CardType.SkillA:
            case CardType.SkillS:
                break;
            default:
                Debug.LogError($"[卡牌] 未识别的词条类型：{type}");
                break;
        }
    }

    // 阵营累加：多名防守方的建筑类词条按份数叠加
    public void AddInto(CardBonus other)
    {
        luck += other.luck;
        healthPercent += other.healthPercent;
        attackFlat += other.attackFlat;
        critRate += other.critRate;
        critDamage += other.critDamage;
        damageVsPlayer += other.damageVsPlayer;
        damageVsCoral += other.damageVsCoral;
        damageVsBuilding += other.damageVsBuilding;
        towerAttack += other.towerAttack;
        guardReduction += other.guardReduction;
        damageBelowHalfHp += other.damageBelowHalfHp;
        damageVsHigherHp += other.damageVsHigherHp;
    }
}
