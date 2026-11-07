using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    using Gen = ConfigManager.TerrainGenerationConfig;

    // 只按约定产出摆放数据：地面以 cell_size 见方、底面中心对齐格心；围墙沿 cell_size 边长、默认沿 Z 延伸
    public static class BuildingDecorator
    {
        public static int BuildPlan(BuildingField f, BuildingKitSet kit, List<TerrainItemData> items)
        {
            if (kit == null) return 0;

            float c = Gen.cell_size;
            // 0 号格中心的地图坐标；摆放数据直接存地图坐标，不以地形中心为原点
            float mapMin = ConfigManager.GlobalVariableConfig.terrain_size_min;
            float ox = mapMin + c * 0.5f;
            float oz = mapMin + c * 0.5f;
            float surface = Gen.ground_height;
            int count = 0;

            // 地面：每格一块，底面中心对齐格子中心
            for (int x = 0; x < f.SizeX; x++)
            {
                for (int z = 0; z < f.SizeZ; z++)
                {
                    if (f.Layer[x, z] < 0) continue;
                    count += Add(kit, items, TerrainItemKind.Ground, f.RegionGroup(x, z),
                        new Vector3(x * c + ox, 0f, z * c + oz), 0f);
                }
            }

            // 围墙：沿边界边铺设；素材默认沿 Z 延伸，所以东西向的边转 90 度
            // 同时收集每段栏杆的两个端点，保证相邻栏杆之间必有石柱
            var verts = new List<Vector2Int>();
            var vertGroups = new List<int>();
            var seenVert = new HashSet<long>();

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
                        if (!f.NeedWallAt(x, z, d)) continue;

                        switch (d)
                        {
                            case 0:
                                AddVert(verts, vertGroups, seenVert, x, z + 1, group);
                                AddVert(verts, vertGroups, seenVert, x + 1, z + 1, group);
                                break;
                            case 1:
                                AddVert(verts, vertGroups, seenVert, x + 1, z, group);
                                AddVert(verts, vertGroups, seenVert, x + 1, z + 1, group);
                                break;
                            case 2:
                                AddVert(verts, vertGroups, seenVert, x, z, group);
                                AddVert(verts, vertGroups, seenVert, x + 1, z, group);
                                break;
                            default:
                                AddVert(verts, vertGroups, seenVert, x, z, group);
                                AddVert(verts, vertGroups, seenVert, x, z + 1, group);
                                break;
                        }

                        int yaw = (d % 2 == 0 ? 90 : 0) + kit.fenceYawOffset;
                        Vector3 mid;
                        switch (d)
                        {
                            case 0: mid = new Vector3((x0 + x1) * 0.5f, surface, z1); break;
                            case 1: mid = new Vector3(x1, surface, (z0 + z1) * 0.5f); break;
                            case 2: mid = new Vector3((x0 + x1) * 0.5f, surface, z0); break;
                            default: mid = new Vector3(x0, surface, (z0 + z1) * 0.5f); break;
                        }

                        count += Add(kit, items, TerrainItemKind.Fence, group, mid, yaw);
                    }
                }
            }

            // 石柱：每段栏杆的两端各一个，重合的格角只放一个
            // 支撑：只生成在高石柱下方，从地块下表面往下
            for (int i = 0; i < verts.Count; i++)
            {
                Vector2Int v = verts[i];
                int group = vertGroups[i];
                var pos = new Vector3(v.x * c + ox - c * 0.5f, surface, v.y * c + oz - c * 0.5f);

                GameObject tallPrefab = kit.UseTallPost(group) ? kit.PickTallPost(group) : null;
                GameObject shortPrefab = kit.PickShortPost(group);

                if (tallPrefab != null)
                {
                    count += AddPrefab(kit, items, TerrainItemKind.PostTall, tallPrefab, pos, 0f);

                    GameObject supportPrefab = kit.PickSupport(group);
                    if (supportPrefab != null)
                        count += AddPrefab(kit, items, TerrainItemKind.Support, supportPrefab,
                            new Vector3(pos.x, 0f, pos.z), 0f);
                }
                else
                {
                    count += AddPrefab(kit, items, TerrainItemKind.PostShort, shortPrefab, pos, 0f);
                }
            }

            return count;
        }

        static void AddVert(List<Vector2Int> verts, List<int> groups, HashSet<long> seen, int cx, int cz, int group)
        {
            if (!seen.Add(((long)cx << 32) | (uint)cz)) return;
            verts.Add(new Vector2Int(cx, cz));
            groups.Add(group);
        }

        static int Add(BuildingKitSet kit, List<TerrainItemData> items, TerrainItemKind kind, int group, Vector3 pos, float yaw)
        {
            return AddPrefab(kit, items, kind, kit.PickFrom(kit.GetList(kind), group), pos, yaw);
        }

        static int AddPrefab(BuildingKitSet kit, List<TerrainItemData> items, TerrainItemKind kind, GameObject prefab,
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
