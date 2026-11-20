using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
namespace TerrainGen
{
    // 地形点位与地形查询的唯一入口：点位来自烘焙数据，高度与刷新合法性一律走向下射线
    public static class TerrainSpawns
    {
        #region 地图范围（直接取配表，不缓存）
        public static float MapMin => GlobalVariable.RangeMin(global_variablesEnum.terrain_size);
        public static float MapSize => GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
        public static float MapMax => MapMin + MapSize;
        public static float ChunkSize => GlobalVariable.F(global_variablesEnum.terrain_chunk_size);
        public static int ChunkCount => GlobalVariable.I(global_variablesEnum.terrain_chunk_size_count);
        public static Vector3 MapCenter => new Vector3(MapMin + MapSize * 0.5f, 0f, MapMin + MapSize * 0.5f);
        public static float SurfaceY => TerrainGeneration.F(terrain_generationEnum.ground_height);
        #endregion

        #region 点位
        public static bool Built { get; private set; }
        public static Vector3 MainGuard { get; private set; }
        public static readonly List<Vector3> SubGuards = new List<Vector3>();
        public static readonly List<Vector3> Towers = new List<Vector3>();
        public static readonly List<Vector3> NeutralStatues = new List<Vector3>();
        public static Vector3 DefenseSpawn { get; private set; }
        public static Vector3 AttackSpawn { get; private set; }

        // 地板格白名单：由烘焙数据里的 Ground 物件还原，刷新点必须落在真实地板上
        private static bool[,] floorCells;
        private static int floorSizeX, floorSizeZ;
        private static float floorCellSize;

        public static void Build(TerrainPlanData data)
        {
            if (data == null)
            {
                Debug.LogError("[地形] TerrainSpawns 构建失败：数据为空");
                return;
            }

            Reset();
            BuildFloorGrid(data);

            float cell = data.cellSize;
            int mainGuardCount = 0;
            int attackCount = 0;
            int defenseCount = 0;

            for (int i = 0; i < data.points.Count; i++)
            {
                MapPoint p = data.points[i];
                var world = new Vector3(MapMin + (p.Cell.x + 0.5f) * cell, SurfaceY, MapMin + (p.Cell.y + 0.5f) * cell);
                switch (p.Kind)
                {
                    case PointKind.MainGuard:
                        MainGuard = world;
                        mainGuardCount++;
                        break;
                    case PointKind.SubGuard:
                        SubGuards.Add(world);
                        break;
                    case PointKind.Tower:
                        Towers.Add(world);
                        break;
                    case PointKind.NeutralStatue:
                        NeutralStatues.Add(world);
                        break;
                    case PointKind.AttackSpawn:
                        AttackSpawn = world;
                        attackCount++;
                        break;
                    case PointKind.DefenseSpawn:
                        DefenseSpawn = world;
                        defenseCount++;
                        break;
                }
            }

            if (mainGuardCount != 1)
                Debug.LogError($"[地形] 烘焙数据里有 {mainGuardCount} 个主守护点，应当恰好 1 个：国王雕像无法定位");
            if (attackCount != 1)
                Debug.LogError($"[地形] 烘焙数据里有 {attackCount} 个进攻出生点，应当恰好 1 个");
            if (defenseCount != 1)
                Debug.LogError($"[地形] 烘焙数据里有 {defenseCount} 个防守出生点，应当恰好 1 个");
            if (SubGuards.Count == 0)
                Debug.LogError("[地形] 烘焙数据里没有副守护点：贤者雕像无法定位");
            if (NeutralStatues.Count == 0)
                Debug.LogWarning("[地形] 烘焙数据里没有中立雕像点位：中立雕像不会生成");

            Built = true;
        }

        public static void Reset()
        {
            Built = false;
            MainGuard = Vector3.zero;
            DefenseSpawn = Vector3.zero;
            AttackSpawn = Vector3.zero;
            SubGuards.Clear();
            Towers.Clear();
            NeutralStatues.Clear();
            floorCells = null;
            floorSizeX = 0;
            floorSizeZ = 0;
            floorCellSize = 0f;
        }

        // 地面物件是每格一块、底面中心对齐格心，所以按它的坐标就能还原整张地板格表
        private static void BuildFloorGrid(TerrainPlanData data)
        {
            floorCellSize = data.cellSize;
            floorSizeX = data.sizeX;
            floorSizeZ = data.sizeZ;
            floorCells = new bool[floorSizeX, floorSizeZ];

            for (int i = 0; i < data.items.Count; i++)
            {
                TerrainItemData item = data.items[i];
                if (item.kind != TerrainItemKind.Ground) continue;
                int x = Mathf.FloorToInt((item.position.x - MapMin) / floorCellSize);
                int z = Mathf.FloorToInt((item.position.z - MapMin) / floorCellSize);
                if (x < 0 || z < 0 || x >= floorSizeX || z >= floorSizeZ) continue;
                floorCells[x, z] = true;
            }
        }

        public static bool IsFloorCell(float x, float z)
        {
            if (floorCells == null)
            {
                LogOnceNoFloorGrid();
                return false;
            }
            int gx = Mathf.FloorToInt((x - MapMin) / floorCellSize);
            int gz = Mathf.FloorToInt((z - MapMin) / floorCellSize);
            return gx >= 0 && gz >= 0 && gx < floorSizeX && gz < floorSizeZ && floorCells[gx, gz];
        }
        #endregion

        #region 地面射线（GroundLayer，命中高度贴合上表面 ±0.5 才算合法）
        private const float SurfaceTolerance = 0.5f;
        private const int MaxSampleAttempts = 32;

        private static bool warnedNoLayer;

        // 地板格表缺失只在首次报错：Build 没跑（或烘焙数据里没有地面）时所有刷新点都会失败
        private static void LogOnceNoFloorGrid()
        {
            if (warnedNoFloorGrid) return;
            warnedNoFloorGrid = true;
            Debug.LogError("[地形] 地板格表为空：TerrainSpawns.Build 没有拿到地面数据，刷新点全部不可用");
        }

        private static bool warnedNoFloorGrid;

        public static bool TrySampleGroundY(float x, float z, out float y)
        {
            y = 0f;

            int layer = Tool.InfoManager != null ? Tool.InfoManager.ground_layer : 0;
            if (layer == 0)
            {
                if (!warnedNoLayer)
                {
                    Debug.LogError("[地形] InfoManager.ground_layer 未配置（0）：地面射线无法进行，刷新点与贴地全部失效");
                    warnedNoLayer = true;
                }
                return false;
            }

            float top = SurfaceY + GlobalVariable.RangeLength(global_variablesEnum.terrain_height);
            var origin = new Vector3(x, top, z);
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, top + 1f, 1 << layer, QueryTriggerInteraction.Ignore))
                return false;

            y = hit.point.y;
            return true;
        }

        // 只给地面高度，人形抬升由 BattleManager.SpawnEntity 统一处理
        public static bool TryGetGroundPos(float x, float z, out Vector3 pos)
        {
            pos = default;
            if (!TrySampleGroundY(x, z, out float y)) return false;
            pos = new Vector3(x, y, z);
            return true;
        }

        public static bool IsLegalSpawnXZ(float x, float z)
        {
            return TrySampleLegalPoint(x, z, out _);
        }

        // 一次射线同时给出合法性与命中高度，避免采样时重复投射
        private static bool TrySampleLegalPoint(float x, float z, out float y)
        {
            y = 0f;
            if (x < MapMin || x >= MapMax || z < MapMin || z >= MapMax) return false;
            // 先过地板格表：射线会命中虚空上的建筑/障碍顶面，光凭"命中高度贴近地面"会把怪刷在半空
            if (!IsFloorCell(x, z)) return false;
            if (!TrySampleGroundY(x, z, out y)) return false;
            return Mathf.Abs(y - SurfaceY) <= SurfaceTolerance;
        }

        public static bool TrySampleSpawnPoint(out Vector3 pos)
        {
            pos = default;
            for (int i = 0; i < MaxSampleAttempts; i++)
            {
                float x = MapMin + Random.value * MapSize;
                float z = MapMin + Random.value * MapSize;
                if (!TrySampleLegalPoint(x, z, out float y)) continue;
                pos = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        public static bool TrySampleSpawnPointInRing(Vector3 center, float minRadius, float maxRadius, out Vector3 pos)
        {
            pos = default;
            float minSq = minRadius * minRadius;
            float maxSq = maxRadius * maxRadius;

            for (int i = 0; i < MaxSampleAttempts; i++)
            {
                // 半径按面积均匀（sqrt 修正），否则会明显堆在环带内侧
                float angle = Random.value * (Mathf.PI * 2f);
                float radius = Mathf.Sqrt(Mathf.Lerp(minSq, maxSq, Random.value));
                float x = center.x + Mathf.Cos(angle) * radius;
                float z = center.z + Mathf.Sin(angle) * radius;
                if (!TrySampleLegalPoint(x, z, out float y)) continue;
                pos = new Vector3(x, y, z);
                return true;
            }
            return false;
        }

        public static Vector3 RandomOf(List<Vector3> list)
        {
            return list == null || list.Count == 0 ? MapCenter : list[Random.Range(0, list.Count)];
        }
        #endregion
    }
}
