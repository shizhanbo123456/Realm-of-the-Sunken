using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
// 图鉴与角色特殊能力的数据来源：技能/Buff 读配表，防守方被动暂无配表先硬编码
public static class GuideCatalog
{
    public readonly struct Entry
    {
        public readonly string title;
        public readonly string description;
        public readonly Texture icon;

        public Entry(string title, string description, Texture icon)
        {
            this.title = title;
            this.description = description;
            this.icon = icon;
        }
    }

    // 防守方被动（顺序 = EntityType.Defense 的 value 0~5），策划案有描述但没进配表，先写在这里
    private static readonly string[] DefensePassiveName =
    {
        "被动·伤害上限", "被动·濒死护盾", "被动·教皇守护",
        "被动·暴击麻痹", "被动·苍白印记", "被动·尸潮强化",
    };

    private static readonly string[] DefensePassiveDesc =
    {
        "每次受到的伤害不超过自身生命值上限的 5%",
        "受到命中类伤害结算后，若生命值低于上限的 10%，立即获得 20 秒护盾并回复 1000 生命值（每 60 秒最多触发一次）",
        "入夜时为所有守护雕像添加「教皇守护」，提供减伤",
        "攻击暴击时附加轻微麻痹效果",
        "「苍白之光」叠至 10 层触发冰冻，「苍白之暗」叠至 10 层触发麻痹；两者都超过 8 层且未满 10 层时触发燃烧",
        "提升僵尸刷新时的等级",
    };

    public static bool HasDefensePassive(int defenseValue) => defenseValue >= 0 && defenseValue < DefensePassiveName.Length;

    public static Entry DefensePassive(int defenseValue)
    {
        return new Entry(DefensePassiveName[defenseValue], DefensePassiveDesc[defenseValue], null);
    }

    // 角色特殊能力：进攻方只有初始技能，防守方再加一条被动
    public static List<Entry> BuildCharacterAbilities(entity config, int saveIndex, bool defense)
    {
        var list = new List<Entry>();
        if (config.initialSkills != null)
        {
            for (int i = 0; i < config.initialSkills.Count; i++)
            {
                int id = config.initialSkills[i];
                if (!SkillConfig.content.TryGetValue(id, out var skill)) continue;
                list.Add(new Entry(skill.name, skill.description, SkillIcon(id)));
            }
        }
        if (defense && HasDefensePassive(saveIndex - GlobalConfig.attack_character_count))
        {
            var passive = DefensePassive(saveIndex - GlobalConfig.attack_character_count);
            list.Add(new Entry(passive.title, passive.description, null));
        }
        return list;
    }

    public static List<Entry> BuildSkillEntries()
    {
        var list = new List<Entry>();
        foreach (var pair in SortedKeys(SkillConfig.content))
        {
            var skill = pair.Value;
            string store = skill.storeLimit < 0 ? "无限" : skill.storeLimit.ToString();
            string des = $"{skill.description}\nCD {skill.cd:0.##}s｜库存 {store}";
            list.Add(new Entry(skill.name, des, SkillIcon(skill.key)));
        }
        return list;
    }

    public static List<Entry> BuildBuffEntries()
    {
        var list = new List<Entry>();
        foreach (var pair in SortedKeys(EffectConfig.content))
        {
            var buff = pair.Value;
            list.Add(new Entry(buff.name, buff.description, BuffIcon(buff.iconId)));
        }
        return list;
    }

    private static Texture SkillIcon(int skillId)
    {
        var map = Tool.ResourceManager != null ? Tool.ResourceManager.SkillIconMap : null;
        return map != null && map.TryGetValue(skillId, out var tex) ? tex : null;
    }

    private static Texture BuffIcon(int iconId)
    {
        var map = Tool.ResourceManager != null ? Tool.ResourceManager.BuffIconMap : null;
        return map != null && map.TryGetValue(iconId, out var tex) ? tex : null;
    }

    private static List<KeyValuePair<int, T>> SortedKeys<T>(Dictionary<int, T> source)
    {
        var list = new List<KeyValuePair<int, T>>(source);
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }
}
