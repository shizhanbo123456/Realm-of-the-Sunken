using System.Collections.Generic;

namespace Ros.Config
{
    // 枚举键表的包装层：一律用生成的枚举读取，不再为每个键生成静态字段

    public static class GlobalVariable
    {
        private static Dictionary<global_variablesEnum, global_variables> map;

        internal static void Bake(ConfigManager m) => map = m.global_variablesData;

        public static global_variables Get(global_variablesEnum key) => ConfigBinary.Require(map, key, "global_variables");

        // 单值（float 列）；该键只有列表值时直接抛
        public static float F(global_variablesEnum key)
        {
            float? value = Get(key).value;
            if (value.HasValue) return value.Value;
            throw new System.FormatException($"[配表] global_variables 的 {key} 只有列表值，没有单值");
        }

        public static int I(global_variablesEnum key) => (int)F(key);

        // 列表值（listValue 列）
        public static IReadOnlyList<float> L(global_variablesEnum key) => Get(key).listValue;

        // 「最小;最大」这类区间键的取值
        public static float RangeMin(global_variablesEnum key) => RangeAt(key, 0);

        public static float RangeMax(global_variablesEnum key) => RangeAt(key, 1);

        // 区间跨度 = 最大 - 最小（地形尺寸、地形高度都用这种口径）
        public static float RangeLength(global_variablesEnum key) => RangeAt(key, 1) - RangeAt(key, 0);

        private static float RangeAt(global_variablesEnum key, int index)
        {
            IReadOnlyList<float> list = L(key);
            if (list.Count <= index)
                throw new System.FormatException($"[配表] global_variables 的 {key} 需要至少 {index + 1} 个区间值，实际 {list.Count} 个");
            return list[index];
        }
    }

    public static class TerrainGeneration
    {
        private static Dictionary<terrain_generationEnum, terrain_generation> map;

        internal static void Bake(ConfigManager m) => map = m.terrain_generationData;

        public static terrain_generation Get(terrain_generationEnum key) => ConfigBinary.Require(map, key, "terrain_generation");

        public static float F(terrain_generationEnum key) => Get(key).value;

        public static int I(terrain_generationEnum key) => (int)Get(key).value;

        public static bool B(terrain_generationEnum key) => Get(key).value != 0f;
    }

    // 界面文本统一入口：V 取纯文本，F 按 {0} 占位符填充
    public static class MessageVariable
    {
        private static Dictionary<message_variablesEnum, message_variables> map;

        internal static void Bake(ConfigManager m) => map = m.message_variablesData;

        public static message_variables Get(message_variablesEnum key) => ConfigBinary.Require(map, key, "message_variables");

        public static string V(message_variablesEnum key) => Get(key).value;

        public static string F(message_variablesEnum key, params object[] args) => string.Format(Get(key).value, args);
    }
}
