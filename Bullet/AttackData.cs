using System;

public class AttackData
{
    public ushort shooter;
    public EntityCategory? shooterCategory; // 敌对判定依据；无施法者（场景伤害）为 null
    public bool breakEndure;
    public float knockbackPower;
    public Damageable.IDamageable damageable;
    public Action<EntityEffectController> addEffectEvent;
    public Action<EntityData> onHit;
    public DamageStat stat;                 // 出伤快照（攻击/暴击/暴伤/等级/词条）
    public float outDamageMultiplier = 1f;

    public static AttackData Create(EntityData shooter, bool breakEndure,
        Damageable.IDamageable damageable = null,
        Action<EntityEffectController> addEffectEvent = null, Action<EntityData> onHit = null,
        float knockbackPower = 0f)
    {
        return new AttackData()
        {
            shooter = shooter != null ? shooter.id : (ushort)0,
            shooterCategory = shooter != null ? shooter.type.category : null,
            breakEndure = breakEndure,
            damageable = damageable,
            addEffectEvent = addEffectEvent,
            onHit = onHit,
            stat = shooter != null && shooter.attributes != null ? shooter.attributes.SnapshotDamage() : default,
            // 出伤乘区必须在施放瞬间快照：命中时才取的话，攻击者状态已被后续帧改变
            outDamageMultiplier = shooter != null && shooter.effectController != null
                ? shooter.effectController.GetOutDamageMultiplier()
                : 1f,
            knockbackPower = knockbackPower,
        };
    }

    // 倍率唯一来源是 skill.csv 第 11 列；词条增伤 / 等级压制在命中时按目标结算
    public int GetDamage(EntityData target, out CombatLabelType type, float segmentRate)
    {
        type = CombatLabelType.Normal;
        if (stat.attack <= 0f) return 0;
        float final = stat.attack;
        final *= outDamageMultiplier;                            // 出伤乘区（Buff）
        final *= segmentRate;                                    // 多段伤害倍率（skill.csv 第 11 列）
        if (stat.critRate > 0f && UnityEngine.Random.Range(0f, 100f) < stat.critRate)
        {
            type = CombatLabelType.Crit;
            final *= stat.critDamage / 100f; // 暴击伤害为整数百分比：100 = 不加成，200 = 翻倍
        }
        if (Tool.BattleManager != null) final *= Tool.BattleManager.GetOutgoingDamageMultiplier(stat, target);
        if (final < 1f) return 1;
        return (int)final;
    }
}
