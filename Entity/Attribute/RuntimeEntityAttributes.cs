using UnityEngine;

// 实体实际属性 = 基础（entity.csv）+ 玩家词条（BattleManager，玩家级）+ Buff 增量（EntityEffectController）
// 当前生命也由本类持有：生命上限变化时按比例换算，并把 healthDirty 置脏让服务器立即同步
public class RuntimeEntityAttributes
{
    private readonly EntityData owner;
    private readonly ConfigManager.EntityConfig config;

    private CardBonus card = CardBonus.Empty;       // 所属玩家自己的词条（非玩家实体为空）
    private CardBonus campCard = CardBonus.Empty;   // 所属阵营玩家的词条累加（只给己方建筑用）

    private float health;
    private float maxHealth;

    public RuntimeEntityAttributes(EntityData owner, EntityType type)
    {
        this.owner = owner;
        config = Tool.InfoManager != null ? Tool.InfoManager.GetEntityConfig(type) : default;
        maxHealth = config.health;
        health = maxHealth;
    }

    public bool Alive => health > 0f;
    public float Health => health;
    public float MaxHealth => maxHealth;

    public float BaseAttack => config.attack;
    public float BaseViewDistance => config.viewDistance;
    public int WeaponSlotCount => config.weaponSlotCount;

    public int Attack => config.attack + card.attackFlat + (IsTower ? campCard.towerAttack : 0) + Mathf.RoundToInt(BuffAttack);
    public float CritRate => config.critRate + card.critRate + BuffCritRate;
    public float CritDamage => config.critDamage + card.critDamage + BuffCritDamage;
    public float ViewDistance => config.viewDistance + BuffViewDistance;

    // 每帧：刷新词条引用并重算生命上限；上限变化即按比例换算当前生命并置脏同步
    public void Update()
    {
        RefreshCardBonus();
        float newMax = Mathf.Max(1f, config.health * (1f + card.healthPercent));
        if (Mathf.Approximately(newMax, maxHealth)) return;
        float ratio = maxHealth > 0f ? health / maxHealth : 1f;
        maxHealth = newMax;
        health = newMax * ratio;
        if (owner != null) owner.healthDirty = true;
    }

    public void ApplyDamage(float amount)
    {
        health = Mathf.Max(0f, health - amount);
        if (owner != null) owner.healthDirty = true;
    }

    public void Heal(float amount)
    {
        if (amount <= 0f) return;
        health = Mathf.Min(maxHealth, health + amount);
        if (owner != null) owner.healthDirty = true;
    }

    public void Kill()
    {
        health = 0f;
        if (owner != null) owner.healthDirty = true;
    }

    // 出伤快照：命中判定时才结算，攻击者状态可能已被后续帧改变
    public DamageStat SnapshotDamage()
    {
        return new DamageStat
        {
            attack = Attack,
            critRate = CritRate,
            critDamage = CritDamage,
            level = owner != null ? owner.level : 1,
            card = card,
            healthRatio = maxHealth > 0f ? health / maxHealth : 0f,
        };
    }

    private bool IsTower => owner != null && owner.type.category == EntityCategory.CrystalStatue;

    private float BuffAttack => owner != null && owner.effectController != null ? owner.effectController.BuffAttack : 0f;
    private float BuffCritRate => owner != null && owner.effectController != null ? owner.effectController.BuffCritRate : 0f;
    private float BuffCritDamage => owner != null && owner.effectController != null ? owner.effectController.BuffCritDamage : 0f;
    private float BuffViewDistance => owner != null && owner.effectController != null ? owner.effectController.BuffViewDistance : 0f;

    // 只有玩家角色吃自己的词条、只有己方建筑吃阵营词条，其余实体跳过查表
    private void RefreshCardBonus()
    {
        var bm = Tool.BattleManager;
        if (bm == null || owner == null) return;
        if (owner.type.category == EntityCategory.Character_Attack || owner.type.category == EntityCategory.Character_Defense)
            card = bm.GetEntityCardBonus(owner);
        if (IsTower || owner.type.category == EntityCategory.GuardStatue)
            campCard = bm.GetCampCardBonus(owner.camp);
    }
}

// 命中瞬间的出伤快照（值类型，不产生 GC）
public struct DamageStat
{
    public float attack;
    public float critRate;
    public float critDamage;
    public int level;
    public float healthRatio;   // 攻击者出手时的生命比例（斗士/反击判定用）
    public CardBonus card;
}
