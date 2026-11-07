using UnityEngine;

namespace TerrainGen
{
    // 生成入口：编辑器非播放态只烘焙数据到 StreamingAssets；播放态优先读缓存，缺失才运行期生成
    public static class TerrainGenerator
    {
        // 客户端：内容根 + 点位由生成层建好，物件交给 Culler 按区块流式取还
        public static TerrainPropCuller Generate(BuildingSettings settings, BuildingKitSet kit, out Transform contentRoot)
        {
            contentRoot = null;
            TerrainPlanData data = LoadData(settings, kit);
            if (data == null) return null;

            TerrainSpawns.Build(data);
            contentRoot = CreateRoot();

            var cullerGo = new GameObject(TerrainAssembler.CullerName);
            cullerGo.transform.position = Vector3.zero;

            var culler = cullerGo.AddComponent<TerrainPropCuller>();
            culler.Initialize(data, kit, contentRoot);

            TerrainAssembler.CreatePoints(data, contentRoot);
            return culler;
        }

        // 服务器：一次性铺设整张地形，只留碰撞体；不建 Culler（服务器不做剔除）
        public static void GenerateServer(BuildingSettings settings, BuildingKitSet kit, out Transform contentRoot)
        {
            contentRoot = null;
            TerrainPlanData data = LoadData(settings, kit);
            if (data == null) return;

            TerrainSpawns.Build(data);
            contentRoot = CreateRoot();
            TerrainAssembler.SpawnColliderOnly(data, kit, contentRoot);
            TerrainAssembler.CreatePoints(data, contentRoot);
        }

        // 数据本身即地图坐标（最小角落在 MapMin），内容根不做平移
        static Transform CreateRoot()
        {
            var root = new GameObject(TerrainAssembler.RootName);
            root.transform.position = Vector3.zero;
            return root.transform;
        }

        static TerrainPlanData LoadData(BuildingSettings settings, BuildingKitSet kit)
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings");
                return null;
            }

            if (!Application.isPlaying)
            {
                Bake(settings, kit);
                return null;
            }

            string path = TerrainPlanData.PathFor(settings.seed, settings.version);
            TerrainPlanData data = TerrainPlanData.TryLoad(path);
            if (data == null)
            {
                Debug.LogWarning($"[地形] 未找到 {path}，改为运行期生成（不写盘）");
                data = BuildData(settings, kit);
            }
            return data;
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
