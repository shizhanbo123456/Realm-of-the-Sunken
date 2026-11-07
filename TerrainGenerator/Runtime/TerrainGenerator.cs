using UnityEngine;

namespace TerrainGen
{
    // 生成入口：编辑器非播放态只烘焙数据到 StreamingAssets；播放态优先读缓存，缺失才运行期生成
    public static class TerrainGenerator
    {
        public static TerrainPropCuller Generate(BuildingSettings settings, BuildingKitSet kit, Transform parent = null)
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings");
                return null;
            }

            string path = TerrainPlanData.PathFor(settings.seed, settings.version);

            if (!Application.isPlaying)
            {
                Bake(settings, kit);
                return null;
            }

            TerrainPlanData data = TerrainPlanData.TryLoad(path);
            if (data == null)
            {
                Debug.LogWarning($"[地形] 未找到 {path}，改为运行期生成（不写盘）");
                data = BuildData(settings, kit);
            }
            if (data == null) return null;

            // 地形局部坐标以自身中心为原点，抬半个尺寸后生成区域的最小角正好落在配表的地形起点
            var origin = new Vector3(
                Landscape.MapMin + data.sizeX * data.cellSize * 0.5f,
                0f,
                Landscape.MapMin + data.sizeZ * data.cellSize * 0.5f);

            // 内容挂到 parent 下；Culler 是场景里的独立物体：不挂父级，也不允许有子物体
            var root = new GameObject(TerrainAssembler.RootName);
            root.transform.SetParent(parent, false);
            root.transform.position = origin;

            var cullerGo = new GameObject(TerrainAssembler.CullerName);
            cullerGo.transform.position = origin;

            var culler = cullerGo.AddComponent<TerrainPropCuller>();
            culler.Initialize(data, kit, root.transform);
            return culler;
        }

        public static TerrainPlanData Bake(BuildingSettings settings, BuildingKitSet kit)
        {
            TerrainPlanData data = BuildData(settings, kit);
            if (data == null) return null;

            string path = TerrainPlanData.PathFor(settings.seed, settings.version);
            if (data.Save(path))
                Debug.Log($"[地形] 已烘焙 {path}：{data.items.Count} 个物件、{data.points.Count} 个点位");
            return data;
        }

        public static TerrainPlanData BuildData(BuildingSettings settings, BuildingKitSet kit)
        {
            if (settings == null) return null;

            var field = new BuildingField();
            field.Generate(settings, settings.seed);
            return TerrainAssembler.BuildPlan(field, settings, kit);
        }
    }
}
