using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 建筑物 / 装饰 / 灌木 / 摆件按面积密度散落（后放的避让先放的），藤蔓贴在地块临空外墙
    public static class PropPlacer
    {
        public const string PropsName = "Props";
        public const string VinesName = "Vines";

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
            count += Scatter(f, s, kit, propsParent, kit.decorations, "Decor_", s.cellsPerDecoration, false, true, ox, oz, rng, spots, cache);
            count += Scatter(f, s, kit, propsParent, kit.bushes, "Bush_", s.cellsPerBush, true, true, ox, oz, rng, spots, cache);
            count += Scatter(f, s, kit, propsParent, kit.props, "Prop_", s.cellsPerProp, false, true, ox, oz, rng, spots, cache);
            count += PlaceVines(f, s, kit, parent, ox, oz, rng);
            return count;
        }

        static int Scatter(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent,
            List<GameObject> list, string prefix, int cellsPer, bool freeYaw, bool jitter,
            float ox, float oz, System.Random rng, List<Spot> spots, Dictionary<GameObject, Bounds> cache)
        {
            if (parent == null || list == null || list.Count == 0 || cellsPer <= 0) return 0;

            int target = Mathf.Max(1, Mathf.RoundToInt(f.FloorCellCount() / (float)cellsPer));
            var cells = new List<Vector2Int>();
            for (int x = 0; x < f.SizeX; x++)
                for (int z = 0; z < f.SizeZ; z++)
                    if (f.Layer[x, z] >= 0) cells.Add(new Vector2Int(x, z));

            for (int i = cells.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                Vector2Int tmp = cells[i];
                cells[i] = cells[j];
                cells[j] = tmp;
            }

            int made = 0;
            for (int i = 0; i < cells.Count && made < target; i++)
            {
                Vector2Int cell = cells[i];
                int group = f.RegionGroup(cell.x, cell.y);
                GameObject prefab = kit.PickFrom(list, group);
                if (prefab == null) continue;
                if (!PrefabBounds.TryGet(prefab, cache, out Bounds b)) continue;

                float radius = Mathf.Max(b.extents.x, b.extents.z);
                float spread = jitter ? Mathf.Max(0f, s.cellSize * 0.5f - radius) : 0f;
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
    }
}
