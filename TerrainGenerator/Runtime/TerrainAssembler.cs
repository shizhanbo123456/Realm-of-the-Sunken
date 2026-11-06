using UnityEngine;

namespace TerrainGen
{
    public static class TerrainAssembler
    {
        public const string RootName = "TerrainRoot";

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
                cellSize = s.cellSize,
                groundHeight = s.groundHeight,
                sizeX = field.SizeX,
                sizeZ = field.SizeZ,
            };

            if (kit != null)
            {
                BuildingDecorator.BuildPlan(field, s, kit, data.items);
                PropPlacer.PlacePlan(field, s, kit, data.items);
            }

            data.points.AddRange(field.Points);
            return data;
        }
    }
}
