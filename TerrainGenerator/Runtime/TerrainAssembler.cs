using UnityEngine;

namespace TerrainGen
{
    using Gen = ConfigManager.TerrainGenerationConfig;

    public static class TerrainAssembler
    {
        public const string RootName = "TerrainRoot";
        public const string CullerName = "TerrainCuller";

        // 布局 → 摆放数据：只产出数据，不实例化任何预制体
        public static TerrainPlanData BuildPlan(BuildingField field, BuildingSettings s, BuildingKitSet kit)
        {
            if (field == null || field.Layer == null)
            {
                Debug.LogError("[地形] 布局为空，先生成布局");
                return null;
            }
            if (s == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings");
                return null;
            }

            var data = new TerrainPlanData
            {
                seed = s.seed,
                configVersion = s.version,
                cellSize = Gen.cell_size,
                groundHeight = Gen.ground_height,
                sizeX = field.SizeX,
                sizeZ = field.SizeZ,
            };

            if (kit != null)
            {
                BuildingDecorator.BuildPlan(field, kit, data.items);
                PropPlacer.PlacePlan(field, kit, data.items, s.seed);
            }

            data.points.AddRange(field.Points);
            Debug.Log($"[地形] 摆放数据完成：{data.items.Count} 个物件、{data.points.Count} 个点位；"
                      + "尺寸只读取根物体 BoxCollider（O(1)），未做任何网格计算");
            return data;
        }
    }
}
