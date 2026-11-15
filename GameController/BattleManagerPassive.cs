using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
public partial class BattleManager
{
    private bool zombieSpawnLevelBoosted;

    private readonly HashSet<int> defenseCharacters = new();

    // 瘟疫使者「低血挂盾」的冷却：按玩家角色记（复活后仍占用），无归属实体按 id 记
    private readonly Dictionary<int, float> plagueGuardReadyTime = new();

    #region 开局全局参数（一次结算）
    private void ApplyGlobalPassives()
    {
        defenseCharacters.Clear();
        foreach (var e in EntityContainer.Entities)
        {
            if (e != null && e.Alive && e.type.category == EntityCategory.Character_Defense) defenseCharacters.Add(e.type.value);
        }
        GmOnBattleStart(); // GM：清掉上一局的无敌记录

        zombieSpawnLevelBoosted = DefenseHas(GlobalConfig.defense_index_death_stroller);

        // 入夜才施加教皇守护，这里按当前昼夜补一次，避免开战即夜晚时漏掉
        if (DefenseHas(GlobalConfig.defense_index_masked_pope)) ApplyPopeGuard(EnvironmentManager.IsDay);
    }

    private bool DefenseHas(int defenseValue) => defenseCharacters.Contains(defenseValue);
    #endregion

    #region 受击被动（白眼伯爵 / 瘟疫使者）
    // 白眼伯爵：单次受到伤害不超过生命上限的固定比例，在护盾与减伤之前就裁到上限
    public float AdjustIncomingDamage(EntityData victim, float damage)
    {
        if (victim == null || damage <= 0f) return damage;
        if (victim.type != EntityType.Defense(GlobalConfig.defense_index_count_eye)) return damage;
        float capRatio = GlobalVariable.F(global_variablesEnum.passive_defense_count_eye_damage_cap_ratio);
        float cap = victim.attributes.MaxHealth * capRatio;
        return damage > cap ? cap : damage;
    }

    // 瘟疫使者：命中类伤害结算后生命低于上限比例就挂护盾（不改伤害、不挡死亡）；每角色每冷却一次
    public void CheckLowHealthShield(EntityData victim, CombatLabelType damageType)
    {
        if (victim == null || !victim.Alive) return;
        if (victim.type != EntityType.Defense(GlobalConfig.defense_index_plague_bringer)) return;
        // 只有命中类伤害参与判定，buff 伤害（DoT / 反伤 / 瘟疫引爆）不触发
        if (damageType != CombatLabelType.Normal && damageType != CombatLabelType.Crit) return;

        float triggerRatio = GlobalVariable.F(global_variablesEnum.passive_defense_plague_shield_trigger_ratio);
        float health = victim.attributes.Health;
        if (health <= 0f || health >= victim.attributes.MaxHealth * triggerRatio) return;

        int key = EntityOwnerClient.TryGetValue(victim.id, out int owner) ? owner : 100000 + victim.id;
        if (plagueGuardReadyTime.TryGetValue(key, out float readyTime) && Time.time < readyTime) return;

        plagueGuardReadyTime[key] = Time.time + GlobalVariable.F(global_variablesEnum.passive_defense_plague_shield_cooldown);
        if (victim.effectController != null)
            victim.effectController.AddEffect(EffectType.Shield,
                GlobalVariable.I(global_variablesEnum.passive_defense_plague_shield_level),
                GlobalVariable.F(global_variablesEnum.passive_defense_plague_shield_duration));
        victim.attributes.Heal(GlobalVariable.F(global_variablesEnum.passive_defense_plague_shield_heal));
        victim.healthDirty = true;
    }
    #endregion

    #region 僵尸刷新等级（按战斗剩余时间实时计算，普通/精英共用）
    public int GetZombieSpawnLevel()
    {
        float elapsed = Mathf.Clamp01((GlobalVariable.F(global_variablesEnum.battle_duration) - BattleRemainTime) / GlobalVariable.F(global_variablesEnum.battle_duration));
        int level = GlobalVariable.I(global_variablesEnum.zombie_spawn_level)
            + Mathf.FloorToInt(elapsed / GlobalVariable.F(global_variablesEnum.zombie_level_step_ratio));
        if (zombieSpawnLevelBoosted) level += GlobalVariable.I(global_variablesEnum.passive_defense_zombie_spawn_level_boost);
        return level;
    }
    #endregion

    #region 事件驱动被动
    public void OnHitPassive(EntityData attacker, EntityData target, CombatLabelType type)
    {
        // 鹿铠怪人 被动「攻击暴击时附加轻微麻痹」：目标已死则不再挂状态
        if (type != CombatLabelType.Crit || attacker == null || target == null || !target.Alive) return;
        if (attacker.type != EntityType.Defense(GlobalConfig.defense_index_deer_knight)) return;
        // 麻痹等级固定 1 级：该 Buff 的等级不参与任何效果计算
        if (target.effectController != null)
            target.effectController.AddEffect(EffectType.Stun, 1,
                GlobalVariable.F(global_variablesEnum.passive_defense_crit_paralysis_duration), attacker);
    }

    private void OnDayNightFlipped(bool isDay)
    {
        if (!AtServer || !BattleStarted) return;
        if (!DefenseHas(GlobalConfig.defense_index_masked_pope)) return;
        ApplyPopeGuard(isDay);
    }

    private void ApplyPopeGuard(bool isDay)
    {
        foreach (var guardStatue in EntityContainer.GuardStatues)
        {
            if (guardStatue == null || guardStatue.effectController == null) continue;
            if (isDay)
            {
                guardStatue.effectController.RemoveEffect(EffectType.PopeGuard);
            }
            else
            {
                guardStatue.effectController.AddEffect(EffectType.PopeGuard, 1, float.MaxValue);
            }
        }
    }

    // source = 刚叠层的苍白标记的施加者，转化出的苍白之冰/雷/火归属它
    public void CheckPaleConversion(EntityData owner, EntityData source)
    {
        if (!AtServer || owner == null || owner.effectController == null) return;
        var effect = owner.effectController;
        int light = effect.GetLevel(EffectType.PaleLight);
        int dark = effect.GetLevel(EffectType.PaleDark);
        int fullStacks = GlobalVariable.I(global_variablesEnum.passive_defense_pale_full_stacks);
        int mixedStacks = GlobalVariable.I(global_variablesEnum.passive_defense_pale_mixed_stacks);
        float controlDuration = GlobalVariable.F(global_variablesEnum.passive_defense_pale_control_duration);
        float burnDuration = GlobalVariable.F(global_variablesEnum.passive_defense_pale_burn_duration);

        if (light >= fullStacks)
        {
            effect.RemoveEffect(EffectType.PaleLight);
            effect.AddEffect(EffectType.PaleIce, 1, controlDuration, source);
        }
        else if (dark >= fullStacks)
        {
            effect.RemoveEffect(EffectType.PaleDark);
            effect.AddEffect(EffectType.PaleThunder, 1, controlDuration, source);
        }
        else if (light >= mixedStacks && dark >= mixedStacks)
        {
            effect.RemoveEffect(EffectType.PaleLight);
            effect.RemoveEffect(EffectType.PaleDark);
            effect.AddEffect(EffectType.PaleFire, 1, burnDuration, source);
        }
    }
    #endregion
}
