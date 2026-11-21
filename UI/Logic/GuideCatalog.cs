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
        public readonly int quality;  // B=0 A=1 S=2；无品质/Buff 为 -1
        public readonly int[] scores; // 6 项，0~4 = D/C/B/A/S；Buff 为 null

        public Entry(string title, string description, Texture icon, int quality = -1, int[] scores = null)
        {
            this.title = title;
            this.description = description;
            this.icon = icon;
            this.quality = quality;
            this.scores = scores;
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
            var icon = SkillIcon(skill.key);
            if (icon == null) continue; // 无图标的是非玩家技能，不进图鉴
            string store;
            if (skill.storeLimit < 0) store = MessageVariable.V(message_variablesEnum.guide_storeUnlimited);
            else store = skill.storeLimit.ToString();
            string des = MessageVariable.F(message_variablesEnum.guide_skillDesc, skill.description, skill.cd, store);
            int[] scores = null;
            if (skill.scores != null) scores = skill.scores.ToArray();
            list.Add(new Entry(skill.name, des, icon, SkillConfig.QualityRank(skill), scores));
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
        Dictionary<int, Texture> map = null;
        if (Tool.ResourceManager != null) map = Tool.ResourceManager.SkillIconMap;
        if (map == null) return null;
        if (map.TryGetValue(skillId, out var tex)) return tex;
        return null;
    }

    private static Texture BuffIcon(int iconId)
    {
        Dictionary<int, Texture> map = null;
        if (Tool.ResourceManager != null) map = Tool.ResourceManager.BuffIconMap;
        if (map == null) return null;
        if (map.TryGetValue(iconId, out var tex)) return tex;
        return null;
    }

    private static List<KeyValuePair<int, T>> SortedKeys<T>(Dictionary<int, T> source)
    {
        var list = new List<KeyValuePair<int, T>>(source);
        list.Sort((a, b) => a.Key.CompareTo(b.Key));
        return list;
    }
}
