using UnityEngine;

namespace TerrainGen
{
    // 生成入口：编辑器非播放态只烘焙数据到 StreamingAssets；播放态优先读缓存，缺失才运行期生成
    public static class TerrainGenerator
    {
        public static TerrainPropCuller Generate(BuildingSettings settings, BuildingKitSet kit, bool culled = true,
            bool colliderOnly = false, bool stripToCollider = false, Transform parent = null)
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

            TerrainSpawns.Build(data);

            // 数据本身即地图坐标（最小角落在 MapMin），内容根不再做平移
            Vector3 origin = Vector3.zero;

            // parent 为空时内容根就建在场景根；Culler 也是场景里的独立物体：不挂父级，也不允许有子物体
            var root = new GameObject(TerrainAssembler.RootName);
            root.transform.SetParent(parent, false);
            root.transform.position = origin;

            var cullerGo = new GameObject(TerrainAssembler.CullerName);
            cullerGo.transform.position = origin;

            var culler = cullerGo.AddComponent<TerrainPropCuller>();
            culler.culled = culled;
            culler.colliderOnly = colliderOnly;
            culler.stripToCollider = stripToCollider;
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
            field.Generate(settings.seed);
            return TerrainAssembler.BuildPlan(field, settings, kit);
        }
    }
}
