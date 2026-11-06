using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 建筑物/装饰/摆件/灌木按面积密度散落，之后是障碍、障碍装饰、漂浮装饰，藤蔓贴外墙
    public static class PropPlacer
    {
        public const string PropsName = "Props";
        public const string VinesName = "Vines";
        public const string ObstaclesName = "Obstacles";
        public const string FloatingName = "Floating";

        struct Spot
        {
            public Vector2 pos;
            public float radius;
        }

        public static int Place(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent)
        {
            if (kit == null) return 0;

            bool wantProps = (kit.buildings.Count > 0 && s.cellsPerBuilding > 0)
                             || (kit.decorations.Count > 0 && s.cellsPerDecoration > 0)
                             || (kit.bushes.Count > 0 && s.cellsPerBush > 0)
                             || (kit.props.Count > 0 && s.cellsPerProp > 0);

            Transform propsParent = null;
            if (wantProps)
            {
                var container = new GameObject(PropsName);
                container.transform.SetParent(parent, false);
                propsParent = container.transform;
            }

            var cache = new Dictionary<GameObject, Bounds>();
            var rng = new System.Random(s.seed ^ 0x51A7C3);
            var spots = new List<Spot>();

            float ox = (f.SizeX - 1) * 0.5f * s.cellSize;
            float oz = (f.SizeZ - 1) * 0.5f * s.cellSize;

            int count = 0;
            count += Scatter(f, s, kit, propsParent, kit.buildings, "Building_", s.cellsPerBuilding, false, false, ox, oz, rng, spots, cache);
            int buildingCount = spots.Count;
            count += Scatter(f, s, kit, propsParent, kit.decorations, "Decor_", s.cellsPerDecoration, false, s.randomPropPosition, ox, oz, rng, spots, cache);
            count += Scatter(f, s, kit, propsParent, kit.props, "Prop_", s.cellsPerProp, false, s.randomPropPosition, ox, oz, rng, spots, cache);
            count += Scatter(f, s, kit, propsParent, kit.bushes, "Bush_", s.cellsPerBush, true, s.randomPropPosition, ox, oz, rng, spots, cache);
            count += PlaceVines(f, s, kit, parent, ox, oz, rng);

            var obstacles = new List<Spot>();
            count += PlaceObstacles(f, s, kit, parent, ox, oz, rng, obstacles, cache);
            count += PlaceObstacleDecor(f, s, kit, parent, ox, oz, rng, cache);
            count += PlaceFloating(f, s, kit, parent, ox, oz, rng, spots, buildingCount, obstacles, cache);
            return count;
        }

        static int Scatter(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent,
            List<GameObject> list, string prefix, int cellsPer, bool freeYaw, bool randomSpot,
            float ox, float oz, System.Random rng, List<Spot> spots, Dictionary<GameObject, Bounds> cache)
        {
            if (parent == null || list == null || list.Count == 0 || cellsPer <= 0) return 0;

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
                float spread = randomSpot ? Mathf.Max(0f, s.cellSize * 0.5f - radius) : 0f;
                float jx = spread > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * spread : 0f;
                float jz = spread > 0f ? (float)(rng.NextDouble() * 2.0 - 1.0) * spread : 0f;
                var pos = new Vector3(cell.x * s.cellSize - ox + jx, s.groundHeight - b.min.y, cell.y * s.cellSize - oz + jz);
                if (Blocked(pos, radius, s.propGapPadding, spots)) continue;

                var go = (GameObject)Object.Instantiate(prefab, parent);
                go.name = prefix + prefab.name;
                float yaw = freeYaw ? (float)rng.NextDouble() * 360f : rng.Next(4) * 90f;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                go.transform.localPosition = pos;

                spots.Add(new Spot { pos = new Vector2(pos.x, pos.z), radius = radius });
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

        static int PlaceVines(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent,
            float ox, float oz, System.Random rng)
        {
            if (kit.vines == null || kit.vines.Count == 0 || s.vineChance <= 0f) return 0;

            var container = new GameObject(VinesName);
            container.transform.SetParent(parent, false);
            float c = s.cellSize;
            int made = 0;

            for (int x = 0; x < f.SizeX; x++)
            {
                for (int z = 0; z < f.SizeZ; z++)
                {
                    if (f.Layer[x, z] < 0) continue;

                    float x0 = x * c - ox - c * 0.5f;
                    float x1 = x0 + c;
                    float z0 = z * c - oz - c * 0.5f;
                    float z1 = z0 + c;
                    int group = f.RegionGroup(x, z);

                    for (int d = 0; d < 4; d++)
                    {
                        if (!f.IsOuterEdge(x, z, d)) continue;
                        if (rng.NextDouble() >= s.vineChance) continue;

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
                        pos.y = s.groundHeight * (0.5f + 0.5f * (float)rng.NextDouble());

                        var go = (GameObject)Object.Instantiate(prefab, container.transform);
                        go.name = "Vine_" + prefab.name;
                        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        go.transform.localPosition = pos;
                        made++;
                    }
                }
            }
            return made;
        }

        // 障碍：锚在虚空格上，允许压到地面，但不能整块都落在地面上；彼此允许重叠但不超过一半
        static int PlaceObstacles(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent,
            float ox, float oz, System.Random rng, List<Spot> placed, Dictionary<GameObject, Bounds> cache)
        {
            if (kit.obstacles.Count == 0 || s.cellsPerObstacle <= 0) return 0;

            List<Vector2Int> voids = CollectVoidCells(f);
            if (voids.Count == 0)
            {
                Debug.LogWarning("[地形] 地图里没有无地面格，障碍未生成");
                return 0;
            }

            var container = new GameObject(ObstaclesName);
            container.transform.SetParent(parent, false);
            int target = Mathf.Max(1, Mathf.RoundToInt(voids.Count / (float)s.cellsPerObstacle));
            float half = s.cellSize * 0.5f;
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                Vector2Int cell = voids[rng.Next(voids.Count)];
                GameObject prefab = kit.PickFrom(kit.obstacles, f.RegionGroup(cell.x, cell.y));
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z);
                float cx = cell.x * s.cellSize - ox + (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                float cz = cell.y * s.cellSize - oz + (float)(rng.NextDouble() * 2.0 - 1.0) * half;
                var flat = new Vector2(cx, cz);

                if (!TouchesVoid(f, cx, cz, radius, ox, oz, s.cellSize)) continue;

                bool mostlyOverlapped = false;
                for (int k = 0; k < placed.Count; k++)
                    if (MostlyOverlaps(flat, radius, placed[k].pos, placed[k].radius))
                    {
                        mostlyOverlapped = true;
                        break;
                    }
                if (mostlyOverlapped) continue;

                float baseY = Mathf.Lerp(s.obstacleBaseMinY, s.obstacleBaseMaxY, (float)rng.NextDouble());
                var go = (GameObject)Object.Instantiate(prefab, container.transform);
                go.name = "Obstacle_" + prefab.name;
                go.transform.localRotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
                go.transform.localPosition = new Vector3(cx, baseY - b.min.y, cz);

                placed.Add(new Spot { pos = flat, radius = radius });
                made++;
            }
            return made;
        }

        // 障碍装饰：虚空格上随机落点，高度在下表面与上表面之间随机
        static int PlaceObstacleDecor(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent,
            float ox, float oz, System.Random rng, Dictionary<GameObject, Bounds> cache)
        {
            if (kit.obstacleDecorations.Count == 0 || s.cellsPerObstacleDecoration <= 0) return 0;

            List<Vector2Int> voids = CollectVoidCells(f);
            if (voids.Count == 0) return 0;

            var container = new GameObject(ObstaclesName + "Decor");
            container.transform.SetParent(parent, false);
            int target = Mathf.Max(1, Mathf.RoundToInt(voids.Count / (float)s.cellsPerObstacleDecoration));
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                Vector2Int cell = voids[rng.Next(voids.Count)];
                GameObject prefab = kit.PickFrom(kit.obstacleDecorations, f.RegionGroup(cell.x, cell.y));
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z);
                float spread = Mathf.Max(0f, s.cellSize * 0.5f - radius);
                float cx = cell.x * s.cellSize - ox + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                float cz = cell.y * s.cellSize - oz + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
                float y = (float)rng.NextDouble() * s.groundHeight;

                var go = (GameObject)Object.Instantiate(prefab, container.transform);
                go.name = "ObstacleDecor_" + prefab.name;
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localPosition = new Vector3(cx, y, cz);
                made++;
            }
            return made;
        }

        // 漂浮装饰：整张图任意位置，高度在地面之上抬 [0, floatingExtraHeight]，不压障碍和建筑物
        static int PlaceFloating(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent,
            float ox, float oz, System.Random rng, List<Spot> props, int buildingCount, List<Spot> obstacles,
            Dictionary<GameObject, Bounds> cache)
        {
            if (kit.floatingDecorations.Count == 0 || s.cellsPerFloatingDecoration <= 0) return 0;

            var container = new GameObject(FloatingName);
            container.transform.SetParent(parent, false);
            int target = Mathf.Max(1, Mathf.RoundToInt(f.SizeX * f.SizeZ / (float)s.cellsPerFloatingDecoration));
            float halfW = f.SizeX * s.cellSize * 0.5f;
            float halfD = f.SizeZ * s.cellSize * 0.5f;
            int made = 0;

            for (int i = 0; i < target * 40 + 200 && made < target; i++)
            {
                float cx = (float)(rng.NextDouble() * 2.0 - 1.0) * halfW;
                float cz = (float)(rng.NextDouble() * 2.0 - 1.0) * halfD;

                int gx = Mathf.Clamp(Mathf.RoundToInt((cx + ox) / s.cellSize), 0, f.SizeX - 1);
                int gz = Mathf.Clamp(Mathf.RoundToInt((cz + oz) / s.cellSize), 0, f.SizeZ - 1);
                GameObject prefab = kit.PickFrom(kit.floatingDecorations, f.RegionGroup(gx, gz));
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z);
                var flat = new Vector2(cx, cz);
                if (OverlapsAny(flat, radius, obstacles)) continue;
                if (OverlapsAny(flat, radius, props, buildingCount)) continue;

                float bottom = s.groundHeight + (float)rng.NextDouble() * s.floatingExtraHeight;
                var go = (GameObject)Object.Instantiate(prefab, container.transform);
                go.name = "Floating_" + prefab.name;
                go.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                go.transform.localPosition = new Vector3(cx, bottom - b.min.y, cz);
                made++;
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
            int x0 = Mathf.FloorToInt((cx - r + ox) / cell + 0.5f);
            int x1 = Mathf.FloorToInt((cx + r + ox) / cell + 0.5f);
            int z0 = Mathf.FloorToInt((cz - r + oz) / cell + 0.5f);
            int z1 = Mathf.FloorToInt((cz + r + oz) / cell + 0.5f);

            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    if (x < 0 || z < 0 || x >= f.SizeX || z >= f.SizeZ) return true;
                    if (f.Layer[x, z] < 0) return true;
                }
            return false;
        }

        static bool OverlapsAny(Vector2 pos, float radius, List<Spot> others, int count = -1)
        {
            int n = count < 0 ? others.Count : Mathf.Min(count, others.Count);
            for (int i = 0; i < n; i++)
                if (OverlapArea(pos, radius, others[i].pos, others[i].radius) > 0f) return true;
            return false;
        }

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
    }
}
