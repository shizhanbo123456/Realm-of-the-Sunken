using Vector2Int = UnityEngine.Vector2Int;
using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
namespace TerrainGen
{

    // 建筑物/装饰/摆件/灌木按面积密度散落，之后是障碍、障碍装饰、漂浮装饰、鱼群，藤蔓贴外墙
    public static class PropPlacer
    {
        struct Spot
        {
            public Vector2 pos;
            public float radius;
        }

        public static int PlacePlan(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items, int seed)
        {
            if (kit == null) return 0;

            var cache = new Dictionary<GameObject, Bounds>();
            var rng = new System.Random(seed ^ 0x51A7C3);
            var spots = new List<Spot>();

            // 0 号格中心的地图坐标；摆放数据直接存地图坐标，不以地形中心为原点
            float mapMin = GlobalVariable.RangeMin(global_variablesEnum.terrain_size);
            float ox = mapMin + TerrainGeneration.F(terrain_generationEnum.cell_size) * 0.5f;
            float oz = mapMin + TerrainGeneration.F(terrain_generationEnum.cell_size) * 0.5f;

            int count = 0;
            var reserved = new HashSet<long>();
            count += ScatterBuildings(f, kit, items, TerrainGeneration.I(terrain_generationEnum.cells_per_building), ox, oz, rng, spots, cache, reserved);
            count += Scatter(f, kit, items, TerrainItemKind.Decoration, kit.decorations, TerrainGeneration.I(terrain_generationEnum.cells_per_decoration), false, TerrainGeneration.B(terrain_generationEnum.random_prop_position), ox, oz, rng, spots, cache, null);
            count += Scatter(f, kit, items, TerrainItemKind.Prop, kit.props, TerrainGeneration.I(terrain_generationEnum.cells_per_prop), false, TerrainGeneration.B(terrain_generationEnum.random_prop_position), ox, oz, rng, spots, cache, null);
            count += Scatter(f, kit, items, TerrainItemKind.Bush, kit.bushes, TerrainGeneration.I(terrain_generationEnum.cells_per_bush), true, TerrainGeneration.B(terrain_generationEnum.random_prop_position), ox, oz, rng, spots, cache, null);
            count += PlaceVines(f, kit, items, ox, oz, rng);

            var obstacles = new List<Spot>();
            count += PlaceObstacles(f, kit, items, ox, oz, rng, obstacles, reserved, cache);
            count += PlaceObstacleDecor(f, kit, items, ox, oz, rng);
            count += PlaceFloating(f, kit, items, ox, oz, rng, reserved);
            count += PlaceFish(f, kit, items, ox, oz, rng, reserved);
            return count;
        }

        // 建筑只落在虚空格：玩家看到的"建筑群"都长在地块之间的空隙里，底面与地块下表面平齐（y=0）。
        // 压在地板格上的大件会被 NavMesh 当成障碍，把可行走面切成两块，所以这里连轮廓覆盖到的格都必须是虚空
        static int ScatterBuildings(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items, int cellsPer,
            float ox, float oz, System.Random rng, List<Spot> spots, Dictionary<GameObject, Bounds> cache,
            HashSet<long> reserved)
        {
            if (kit.buildings == null || kit.buildings.Count == 0 || cellsPer <= 0) return 0;

            var voids = CollectVoidCells(f);
            if (voids.Count == 0)
            {
                Debug.LogWarning("[地形] 地图里没有无地面格，建筑未生成（建筑只落在虚空上）");
                return 0;
            }

            float c = TerrainGeneration.F(terrain_generationEnum.cell_size);
            int target = Mathf.Max(1, Mathf.RoundToInt(voids.Count / (float)cellsPer));
            for (int i = voids.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                Vector2Int tmp = voids[i];
                voids[i] = voids[j];
                voids[j] = tmp;
            }

            int made = 0;
            for (int i = 0; i < voids.Count && made < target; i++)
            {
                Vector2Int cell = voids[i];
                GameObject prefab = kit.PickFrom(kit.buildings, f.RegionGroup(cell.x, cell.y));
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z);
                float cx = cell.x * c + ox;
                float cz = cell.y * c + oz;
                if (!AllCellsVoid(f, cx, cz, radius, ox, oz, c)) continue;
                if (Blocked(new Vector3(cx, 0f, cz), radius, TerrainGeneration.F(terrain_generationEnum.prop_gap_padding), spots)) continue;

                var pos = new Vector3(cx, -b.min.y, cz); // 底面贴地块下表面
                float yaw = rng.Next(4) * 90f;
                if (Add(kit, items, TerrainItemKind.Building, prefab, pos, yaw) == 0) continue;

                spots.Add(new Spot { pos = new Vector2(cx, cz), radius = radius });
                reserved.Add(CellKey(cell.x, cell.y));
                made++;
            }
            return made;
        }

        static int Scatter(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items,
            TerrainItemKind kind, List<GameObject> list, int cellsPer, bool freeYaw, bool randomSpot,
            float ox, float oz, System.Random rng, List<Spot> spots, Dictionary<GameObject, Bounds> cache,
            HashSet<long> reserved = null)
        {
            if (list == null || list.Count == 0 || cellsPer <= 0) return 0;

            int target = Mathf.Max(1, Mathf.RoundToInt(f.FloorCellCount() / (float)cellsPer));
            var cells = new List<Vector2Int>();
            for (int x = 0; x < f.SizeX; x++)
                for (int z = 0; z < f.SizeZ; z++)
                    if (f.Layer[x, z] >= 0) cells.Add(new Vector2Int(x, z));
            if (cells.Count == 0) return 0;

            if (!randomSpot)
            {
                for (int i = cells.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    Vector2Int tmp = cells[i];
                    cells[i] = cells[j];
                    cells[j] = tmp;
                }
            }

            // 随机落点模式：每次在随机地板格里取随机点，同一格可以放多件，靠间距约束防重叠
            int maxTries = randomSpot ? target * 40 + 200 : cells.Count;
            int made = 0;
            for (int i = 0; i < maxTries && made < target; i++)
            {
                Vector2Int cell = randomSpot ? cells[rng.Next(cells.Count)] : cells[i];
                int group = f.RegionGroup(cell.x, cell.y);
                GameObject prefab = kit.PickFrom(list, group);
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z);
                float spread = randomSpot ? Mathf.Max(0f, TerrainGeneration.F(terrain_generationEnum.cell_size) * 0.5f - radius) : 0f;
                float jx = spread > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * spread : 0f;
                float jz = spread > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * spread : 0f;
                var pos = new Vector3(cell.x * TerrainGeneration.F(terrain_generationEnum.cell_size) + ox + jx, TerrainGeneration.F(terrain_generationEnum.ground_height) - b.min.y, cell.y * TerrainGeneration.F(terrain_generationEnum.cell_size) + oz + jz);
                if (Blocked(pos, radius, TerrainGeneration.F(terrain_generationEnum.prop_gap_padding), spots)) continue;

                float yaw = freeYaw ? (float)rng.NextDouble() * 360f : rng.Next(4) * 90f;
                if (Add(kit, items, kind, prefab, pos, yaw) == 0) continue;

                spots.Add(new Spot { pos = new Vector2(pos.x, pos.z), radius = radius });
                reserved?.Add(CellKey(cell.x, cell.y));
                made++;
            }
            return made;
        }

        static bool Blocked(Vector3 pos, float radius, float padding, List<Spot> spots)
        {
            for (int i = 0; i < spots.Count; i++)
            {
                float dx = pos.x - spots[i].pos.x;
                float dz = pos.z - spots[i].pos.y;
                float need = radius + spots[i].radius + padding;
                if (dx * dx + dz * dz < need * need) return true;
            }
            return false;
        }

        static int PlaceVines(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items,
            float ox, float oz, System.Random rng)
        {
            if (kit.vines == null || kit.vines.Count == 0 || TerrainGeneration.F(terrain_generationEnum.vine_chance) <= 0f) return 0;

            float c = TerrainGeneration.F(terrain_generationEnum.cell_size);
            int made = 0;

            for (int x = 0; x < f.SizeX; x++)
            {
                for (int z = 0; z < f.SizeZ; z++)
                {
                    if (f.Layer[x, z] < 0) continue;

                    float x0 = x * c + ox - c * 0.5f;
                    float x1 = x0 + c;
                    float z0 = z * c + oz - c * 0.5f;
                    float z1 = z0 + c;
                    int group = f.RegionGroup(x, z);

                    for (int d = 0; d < 4; d++)
                    {
                        if (!f.IsOuterEdge(x, z, d)) continue;
                        if (rng.NextDouble() >= TerrainGeneration.F(terrain_generationEnum.vine_chance)) continue;

                        GameObject prefab = kit.PickVine(group);
                        if (prefab == null) continue;

                        int yaw = (d % 2 == 0 ? 90 : 0) + kit.vineYawOffset;
                        Vector3 pos;
                        switch (d)
                        {
                            case 0: pos = new Vector3((x0 + x1) * 0.5f, 0f, z1); break;
                            case 1: pos = new Vector3(x1, 0f, (z0 + z1) * 0.5f); break;
                            case 2: pos = new Vector3((x0 + x1) * 0.5f, 0f, z0); break;
                            default: pos = new Vector3(x0, 0f, (z0 + z1) * 0.5f); break;
                        }
                        // 只在地块上半部分高度贴
                        pos.y = TerrainGeneration.F(terrain_generationEnum.ground_height) * (0.5f + 0.5f * (float)rng.NextDouble());

                        if (Add(kit, items, TerrainItemKind.Vine, prefab, pos, yaw) > 0) made++;
                    }
                }
            }
            return made;
        }

        // 障碍：锚在虚空格上，允许压到地面，但不能整块都落在地面上；彼此允许重叠但不超过一半
        static int PlaceObstacles(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items,
            float ox, float oz, System.Random rng, List<Spot> placed, HashSet<long> reserved, Dictionary<GameObject, Bounds> cache)
        {
            if (kit.obstacles.Count == 0 || TerrainGeneration.I(terrain_generationEnum.cells_per_obstacle) <= 0) return 0;

            List<Vector2Int> voids = CollectVoidCells(f);
            if (voids.Count == 0)
            {
                Debug.LogWarning("[地形] 地图里没有无地面格，障碍未生成");
                return 0;
            }

            int target = Mathf.Max(1, Mathf.RoundToInt(voids.Count / (float)TerrainGeneration.I(terrain_generationEnum.cells_per_obstacle)));
            float half = TerrainGeneration.F(terrain_generationEnum.cell_size) * 0.5f;
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                Vector2Int cell = voids[rng.Next(voids.Count)];
                if (reserved.Contains(CellKey(cell.x, cell.y))) continue; // 建筑已经占了这格虚空，不再叠障碍
                GameObject prefab = kit.PickFrom(kit.obstacles, f.RegionGroup(cell.x, cell.y));
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float scale = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * TerrainGeneration.F(terrain_generationEnum.prop_scale_range);
                float radius = Mathf.Max(b.extents.x, b.extents.z) * scale;
                float cx = cell.x * TerrainGeneration.F(terrain_generationEnum.cell_size) + ox + (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                float cz = cell.y * TerrainGeneration.F(terrain_generationEnum.cell_size) + oz + (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                var flat = new Vector2(cx, cz);

                if (!TouchesVoid(f, cx, cz, radius, ox, oz, TerrainGeneration.F(terrain_generationEnum.cell_size))) continue;

                bool mostlyOverlapped = false;
                for (int k = 0; k < placed.Count; k++)
                    if (MostlyOverlaps(flat, radius, placed[k].pos, placed[k].radius))
                    {
                        mostlyOverlapped = true;
                        break;
                    }
                if (mostlyOverlapped) continue;

                float baseY = Mathf.Lerp(TerrainGeneration.F(terrain_generationEnum.obstacle_base_min_y), TerrainGeneration.F(terrain_generationEnum.obstacle_base_max_y), (float)rng.NextDouble());
                var pos = new Vector3(cx, baseY - b.min.y * scale, cz);
                float yaw = (float)rng.NextDouble() * 360f;
                if (Add(kit, items, TerrainItemKind.Obstacle, prefab, pos, yaw, scale) == 0) continue;

                placed.Add(new Spot { pos = flat, radius = radius });
                reserved.Add(CellKey(cell.x, cell.y));
                made++;
            }
            return made;
        }

        // 障碍装饰：虚空格上随机落点，高度在下表面与上表面之间随机
        static int PlaceObstacleDecor(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items,
            float ox, float oz, System.Random rng)
        {
            if (kit.obstacleDecorations.Count == 0 || TerrainGeneration.I(terrain_generationEnum.cells_per_obstacle_decoration) <= 0) return 0;

            List<Vector2Int> voids = CollectVoidCells(f);
            if (voids.Count == 0) return 0;

            int target = Mathf.Max(1, Mathf.RoundToInt(voids.Count / (float)TerrainGeneration.I(terrain_generationEnum.cells_per_obstacle_decoration)));
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                Vector2Int cell = voids[rng.Next(voids.Count)];
                GameObject prefab = kit.PickFrom(kit.obstacleDecorations, f.RegionGroup(cell.x, cell.y));
                if (prefab == null) continue;

                float scale = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * TerrainGeneration.F(terrain_generationEnum.prop_scale_range);
                float spread = TerrainGeneration.F(terrain_generationEnum.cell_size) * 0.5f;
                float cx = cell.x * TerrainGeneration.F(terrain_generationEnum.cell_size) + ox + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                float cz = cell.y * TerrainGeneration.F(terrain_generationEnum.cell_size) + oz + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                float y = (float)rng.NextDouble() * TerrainGeneration.F(terrain_generationEnum.ground_height);

                if (Add(kit, items, TerrainItemKind.ObstacleDecor, prefab, new Vector3(cx, y, cz),
                        (float)rng.NextDouble() * 360f, scale) > 0) made++;
            }
            return made;
        }

        // 漂浮装饰：整张图任意位置，高度在地面之上抬 [0, floatingExtraHeight]
        // 只避免生成点与障碍/建筑物落在同一格，不做模型层面的重叠检测
        static int PlaceFloating(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items,
            float ox, float oz, System.Random rng, HashSet<long> reserved)
        {
            if (kit.floatingDecorations.Count == 0 || TerrainGeneration.I(terrain_generationEnum.cells_per_floating_decoration) <= 0) return 0;

            int target = Mathf.Max(1, Mathf.RoundToInt(f.SizeX * f.SizeZ / (float)TerrainGeneration.I(terrain_generationEnum.cells_per_floating_decoration)));
            float c = TerrainGeneration.F(terrain_generationEnum.cell_size);
            float halfW = f.SizeX * c * 0.5f;
            float halfD = f.SizeZ * c * 0.5f;
            float centerX = ox + (f.SizeX - 1) * 0.5f * c;
            float centerZ = oz + (f.SizeZ - 1) * 0.5f * c;
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                float cx = centerX + (float)(rng.NextDouble() * 2.0 - 1.0) * halfW;
                float cz = centerZ + (float)(rng.NextDouble() * 2.0 - 1.0) * halfD;

                int gx = Mathf.Clamp(Mathf.RoundToInt((cx - ox) / c), 0, f.SizeX - 1);
                int gz = Mathf.Clamp(Mathf.RoundToInt((cz - oz) / c), 0, f.SizeZ - 1);
                if (reserved.Contains(CellKey(gx, gz))) continue;

                GameObject prefab = kit.PickFrom(kit.floatingDecorations, f.RegionGroup(gx, gz));
                if (prefab == null) continue;

                float scale = 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * TerrainGeneration.F(terrain_generationEnum.prop_scale_range);
                float y = TerrainGeneration.F(terrain_generationEnum.ground_height) + (float)rng.NextDouble() * TerrainGeneration.F(terrain_generationEnum.floating_extra_height);
                if (Add(kit, items, TerrainItemKind.Floating, prefab, new Vector3(cx, y, cz),
                        (float)rng.NextDouble() * 360f, scale) > 0) made++;
            }
            return made;
        }

        // 鱼群：与漂浮装饰同规则（任意位置、地面之上、避开障碍与建筑物的生成点）
        // 不读包围盒，预制体没有 Mesh/碰撞体也能生成，直接按采样点摆放
        static int PlaceFish(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items,
            float ox, float oz, System.Random rng, HashSet<long> reserved)
        {
            if (kit.fishSchools.Count == 0 || TerrainGeneration.I(terrain_generationEnum.cells_per_fish_school) <= 0) return 0;

            int target = Mathf.Max(1, Mathf.RoundToInt(f.SizeX * f.SizeZ / (float)TerrainGeneration.I(terrain_generationEnum.cells_per_fish_school)));
            float c = TerrainGeneration.F(terrain_generationEnum.cell_size);
            float halfW = f.SizeX * c * 0.5f;
            float halfD = f.SizeZ * c * 0.5f;
            float centerX = ox + (f.SizeX - 1) * 0.5f * c;
            float centerZ = oz + (f.SizeZ - 1) * 0.5f * c;
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                float cx = centerX + (float)(rng.NextDouble() * 2.0 - 1.0) * halfW;
                float cz = centerZ + (float)(rng.NextDouble() * 2.0 - 1.0) * halfD;

                int gx = Mathf.Clamp(Mathf.RoundToInt((cx - ox) / c), 0, f.SizeX - 1);
                int gz = Mathf.Clamp(Mathf.RoundToInt((cz - oz) / c), 0, f.SizeZ - 1);
                if (reserved.Contains(CellKey(gx, gz))) continue;

                GameObject prefab = kit.PickFrom(kit.fishSchools, f.RegionGroup(gx, gz));
                if (prefab == null) continue;

                float y = TerrainGeneration.F(terrain_generationEnum.ground_height) + (float)rng.NextDouble() * TerrainGeneration.F(terrain_generationEnum.floating_extra_height);
                if (Add(kit, items, TerrainItemKind.Fish, prefab, new Vector3(cx, y, cz), 0f) > 0) made++;
            }
            return made;
        }

        static List<Vector2Int> CollectVoidCells(BuildingField f)
        {
            var voids = new List<Vector2Int>();
            for (int x = 0; x < f.SizeX; x++)
                for (int z = 0; z < f.SizeZ; z++)
                    if (f.Layer[x, z] < 0) voids.Add(new Vector2Int(x, z));
            return voids;
        }

        // 包围盒脚下的格子里至少有一个没有地面（含地图外），即"不是整个落在地面上"
        static bool TouchesVoid(BuildingField f, float cx, float cz, float r, float ox, float oz, float cell)
        {
            int x0 = Mathf.FloorToInt((cx - r - ox) / cell + 0.5f);
            int x1 = Mathf.FloorToInt((cx + r - ox) / cell + 0.5f);
            int z0 = Mathf.FloorToInt((cz - r - oz) / cell + 0.5f);
            int z1 = Mathf.FloorToInt((cz + r - oz) / cell + 0.5f);

            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (x < 0 || z < 0 || x >= f.SizeX || z >= f.SizeZ) return true;
                    if (f.Layer[x, z] < 0) return true;
                }
            return false;
        }

        // 与 TouchesVoid 同一套格子换算，但要求覆盖到的格**全部**无地面（含地图外的越界一律算不合格）
        static bool AllCellsVoid(BuildingField f, float cx, float cz, float r, float ox, float oz, float cell)
        {
            int x0 = Mathf.FloorToInt((cx - r - ox) / cell + 0.5f);
            int x1 = Mathf.FloorToInt((cx + r - ox) / cell + 0.5f);
            int z0 = Mathf.FloorToInt((cz - r - oz) / cell + 0.5f);
            int z1 = Mathf.FloorToInt((cz + r - oz) / cell + 0.5f);

            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (x < 0 || z < 0 || x >= f.SizeX || z >= f.SizeZ) return false;
                    if (f.Layer[x, z] >= 0) return false;
                }
            return true;
        }

        static long CellKey(int x, int z) => ((long)x << 32) | (uint)z;

        // 按包围盒水平投影近似成正方形算重叠面积
        static float OverlapArea(Vector2 a, float ra, Vector2 b, float rb)
        {
            float w = Mathf.Min(a.x + ra, b.x + rb) - Mathf.Max(a.x - ra, b.x - rb);
            float h = Mathf.Min(a.y + ra, b.y + rb) - Mathf.Max(a.y - ra, b.y - rb);
            return w <= 0f || h <= 0f ? 0f : w * h;
        }

        // 重叠面积占较小那件的比例超过一半才算"大半重叠"
        static bool MostlyOverlaps(Vector2 a, float ra, Vector2 b, float rb)
        {
            float minArea = Mathf.Min(4f * ra * ra, 4f * rb * rb);
            if (minArea <= 0f) return false;
            return OverlapArea(a, ra, b, rb) / minArea >= 0.5f;
        }

        static int Add(BuildingKitSet kit, List<TerrainItemData> items, TerrainItemKind kind, GameObject prefab,
            Vector3 pos, float yaw, float scale = 1f)
        {
            if (prefab == null) return 0;

            int index = kit.IndexOf(kind, prefab);
            if (index < 0) return 0;

            items.Add(new TerrainItemData
            {
                kind = kind,
                prefabIndex = index,
                position = pos,
                yaw = yaw,
                scale = scale,
            });
            return 1;
        }
    }
}
