using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
// 图鉴与角色特殊能力的数据来源：技能、Buff、被动描述全部读配表
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

    // 角色特殊能力：初始技能 + 被动描述（entity.csv 的 passiveSkillDes，仅玩家角色有）
    public static List<Entry> BuildCharacterAbilities(entity config)
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
        // 被动文案整条自带「名称：效果」，description 留空，由 UI 原样显示 title
        if (!string.IsNullOrEmpty(config.passiveSkillDes))
            list.Add(new Entry(config.passiveSkillDes, string.Empty, null));
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
