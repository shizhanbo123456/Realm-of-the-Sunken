using System.Collections.Generic;

namespace Ros.Config
{
    // 各表包装层：Init 时一次性烘培，运行期只读字典，取数据 O(1)
    // content 由 Init 填充，外部只读，严禁修改

    public static class WeaponConfig
    {
        public static readonly Dictionary<int, weapon> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (weapon row in m.weaponData.Values) content.Add(row.key, row);
        }

        public static weapon Get(int key) => ConfigBinary.Require(content, key, "weapon");
    }

    public static class AttrRangeConfig
    {
        public static readonly Dictionary<int, attr_range> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (attr_range row in m.attr_rangeData.Values) content.Add(row.key, row);
        }

        public static attr_range Get(int key) => ConfigBinary.Require(content, key, "attr_range");

        // 属性值映射成 0~1 的填充比例：最小值=空、最大值=满
        public static float Ratio(this attr_range row, float value)
        {
            return UnityEngine.Mathf.Clamp01((value - row.min) / (row.max - row.min));
        }
    }

    public static class EffectConfig
    {
        public static readonly Dictionary<int, effect> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (effect row in m.effectData.Values) content.Add(row.key, row);
        }

        public static effect Get(int key) => ConfigBinary.Require(content, key, "effect");
    }

    public static class NoticeConfig
    {
        public static readonly Dictionary<int, notice> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (notice row in m.noticeData.Values) content.Add(row.key, row);
        }

        public static notice Get(int key) => ConfigBinary.Require(content, key, "notice");
    }

    public static class QuickMessageConfig
    {
        public static readonly Dictionary<int, quick_message> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (quick_message row in m.quick_messageData.Values) content.Add(row.key, row);
        }

        public static quick_message Get(int key) => ConfigBinary.Require(content, key, "quick_message");
    }

    public static class SettingTipsConfig
    {
        public static readonly Dictionary<int, setting_tips> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (setting_tips row in m.setting_tipsData.Values) content.Add(row.key, row);
        }

        public static setting_tips Get(int key) => ConfigBinary.Require(content, key, "setting_tips");
    }

    public static class BattleFuryLayerConfig
    {
        public static readonly Dictionary<int, battle_fury_layer> content = new();
        private static int maxKey;

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            maxKey = 0;
            foreach (battle_fury_layer row in m.battle_fury_layerData.Values)
            {
                content.Add(row.key, row);
                if (row.key > maxKey) maxKey = row.key;
            }
        }

        public static battle_fury_layer Get(int key) => ConfigBinary.Require(content, key, "battle_fury_layer");

        // 超出范围取最近的一层
        public static battle_fury_layer GetClamped(int key) => Get(key < 0 ? 0 : key > maxKey ? maxKey : key);
    }

    public static class ReviveTimeConfig
    {
        public static readonly Dictionary<int, revive_time> content = new();
        private static int maxKey;

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            maxKey = 0;
            foreach (revive_time row in m.revive_timeData.Values)
            {
                content.Add(row.key, row);
                if (row.key > maxKey) maxKey = row.key;
            }
        }

        public static revive_time Get(int key) => ConfigBinary.Require(content, key, "revive_time");

        public static revive_time GetClamped(int key) => Get(key < 0 ? 0 : key > maxKey ? maxKey : key);
    }

    public static class CharacterExpConfig
    {
        public static readonly Dictionary<int, character_upgrade_exp> content = new();
        public static int MaxLevel { get; private set; }

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            MaxLevel = 1;
            foreach (character_upgrade_exp row in m.character_upgrade_expData.Values)
            {
                content.Add(row.key, row);
                if (row.key > MaxLevel) MaxLevel = row.key;
            }
        }

        public static character_upgrade_exp Get(int key) => ConfigBinary.Require(content, key, "character_upgrade_exp");

        // 升到 level 需要的经验；表里没配 = 已到上限
        public static int NeedExp(int level)
        {
            return content.TryGetValue(level, out character_upgrade_exp row) ? row.exp : 0;
        }
    }

    public static class CardGainUpgradeConfig
    {
        public static readonly Dictionary<int, card_gain_upgrade> content = new();
        private static readonly Dictionary<int, int[]> weightsByDigit = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            weightsByDigit.Clear();
            foreach (card_gain_upgrade row in m.card_gain_upgradeData.Values)
            {
                content.Add(row.key, row);
                weightsByDigit[int.Parse(row.postUpgradeLevelSingleDigit, System.Globalization.CultureInfo.InvariantCulture)] = new[] { row.silverCardWeight, row.GoldCardWeight, row.ColourfulCardWeight };
            }
        }

        public static card_gain_upgrade Get(int key) => ConfigBinary.Require(content, key, "card_gain_upgrade");

        // 按升级后的等级取档位权重；本等级没配行返回 null（= 该次升级不掉卡）
        public static int[] WeightsOf(int level)
        {
            return weightsByDigit.TryGetValue(level % 10, out int[] weights) ? weights : null;
        }
    }

    public static class CardOptionConfig
    {
        // 幸运区间：仅当玩家幸运落在闭区间内该卡才进本档卡池；「下界;-1」= 本档不掉
        public struct LuckRange
        {
            public int min;
            public int max;

            public bool Contains(int luck) => luck >= min && luck <= max;
        }

        public static readonly Dictionary<int, card_option> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (card_option row in m.card_optionData.Values) content.Add(row.key, row);
        }

        public static card_option Get(int key) => ConfigBinary.Require(content, key, "card_option");

        public static LuckRange RangeOf(this card_option row, int tier)
        {
            return tier switch
            {
                0 => ToRange(row.silver),
                1 => ToRange(row.gold),
                2 => ToRange(row.holo),
                _ => throw new System.ArgumentOutOfRangeException(nameof(tier), tier, "卡牌档位只有 0 白银 / 1 黄金 / 2 炫彩"),
            };
        }

        // 阵营限制：空 = -1 不限阵营
        public static int Camp(this card_option row) => row.camp ?? -1;

        private static LuckRange ToRange(Vector2Int range) => new LuckRange { min = range.a, max = range.b };
    }

    public static class AssistantExpressionConfig
    {
        // 保留表内顺序：触发事件 + 动画编号相同的连续行即该动画的顺序帧
        public static readonly List<assistant_expression> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            var keys = new List<int>(m.assistant_expressionData.Keys);
            keys.Sort();
            foreach (int key in keys) content.Add(m.assistant_expressionData[key]);
        }
    }

    public static class EntityConfig
    {
        public static readonly Dictionary<EntityType, entity> content = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            foreach (entity row in m.entityData.Values)
                content.Add(new EntityType((EntityCategory)row.category, row.value), row);
        }

        public static entity Get(EntityType type) => ConfigBinary.Require(content, type, "entity");

        // 一行覆盖整类：先按类别规则归一化副键，再精确查；查不到直接抛
        public static entity GetByCategory(EntityType type)
        {
            var key = new EntityType(type.category, NormalizeValue(type));
            if (content.TryGetValue(key, out entity row)) return row;
            throw new System.FormatException($"[配表] entity 缺少实体类型 ({key.category}, {key.value})：{type} 归一化后无对应行");
        }

        private static int NormalizeValue(EntityType type)
        {
            switch (type.category)
            {
                case EntityCategory.Zombie:
                case EntityCategory.EliteZombie:
                case EntityCategory.CrystalStatue:
                    return 0;
                case EntityCategory.GuardStatue:
                    return type.value < GlobalConfig.sage_statue_count ? 0 : GlobalConfig.sage_statue_count;
                default:
                    return type.value;
            }
        }

        public static EntityAttribute GetAttribute(this entity row)
        {
            return new EntityAttribute
            {
                health = row.health,
                attack = row.attack,
                critRate = row.critRate,
                critDamage = row.critDamage,
                viewDistance = row.viewDistance,
                weaponSlotCount = row.weaponSlotCount,
            };
        }
    }

    // 技能附带的一个 Buff：类型 / 等级 / 持续时间
    public struct BuffEntry
    {
        public int type;
        public int level;
        public float duration;
    }

    public static class SkillBuffs
    {
        // skill.buffs 每 3 个数一个 Buff，多个 Buff 连排
        public static int BuffCount(this skill row) => row.buffs.Count / 3;

        public static BuffEntry BuffAt(this skill row, int index)
        {
            if (index < 0 || index >= row.BuffCount())
                throw new System.ArgumentOutOfRangeException(nameof(index), index,
                    "[配表] skill " + row.key + " 只有 " + row.BuffCount() + " 个 Buff");
            int i = index * 3;
            return new BuffEntry
            {
                type = (int)row.buffs[i],
                level = (int)row.buffs[i + 1],
                duration = row.buffs[i + 2],
            };
        }
    }

    public static class SkillConfig
    {        public static readonly Dictionary<int, skill> content = new();
        private static readonly Dictionary<int, int> rankOf = new();

        internal static void Bake(ConfigManager m)
        {
            content.Clear();
            rankOf.Clear();
            foreach (skill row in m.skillData.Values)
            {
                content.Add(row.key, row);
                rankOf.Add(row.key, QualityRankOf(row.quality));
            }
        }

        public static skill Get(int key) => ConfigBinary.Require(content, key, "skill");

        public static int QualityRank(skill row) => rankOf.TryGetValue(row.key, out int rank) ? rank : 0;

        // 品质只有 B/A/S 三档，空 = 无品质（按最低计）
        private static int QualityRankOf(string quality)
        {
            switch (quality.Trim().ToUpperInvariant())
            {
                case "S": return 2;
                case "A": return 1;
                case "B": return 0;
                case "": return 0;
                default: throw new System.FormatException($"[配表] skill 品质无法识别：{quality}（只支持 B / A / S）");
            }
        }
    }
}
