using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 只按约定摆预制体：地面 6×6×4、原点在底面中心；围墙 6 米长、默认沿 Z 延伸
    public static class BuildingDecorator
    {
        public const string Name = "Decor";

        public static int Build(BuildingField f, BuildingSettings s, BuildingKitSet kit, Transform parent)
        {
            if (kit == null) return 0;

            var container = new GameObject(Name);
            container.transform.SetParent(parent, false);

            float c = s.cellSize;
            float ox = (f.SizeX - 1) * 0.5f * c;
            float oz = (f.SizeZ - 1) * 0.5f * c;
            float surface = s.groundHeight;
            int count = 0;

            // 地面：每格一块，底面中心对齐格子中心
            for (int x = 0; x < f.SizeX; x++)
            {
                for (int z = 0; z < f.SizeZ; z++)
                {
                    if (f.Layer[x, z] < 0) continue;
                    GameObject prefab = kit.PickGround(f.RegionGroup(x, z));
                    if (prefab == null) continue;

                    var go = (GameObject)Object.Instantiate(prefab, container.transform);
                    go.name = "Ground_" + prefab.name;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localPosition = new Vector3(x * c - ox, 0f, z * c - oz);
                    count++;
                }
            }

            // 围墙：沿边界边铺设；素材默认沿 Z 延伸，所以东西向的边转 90 度
            // 同时收集每段栏杆的两个端点，保证相邻栏杆之间必有石桩
            var verts = new List<Vector2Int>();
            var vertGroups = new List<int>();
            var seenVert = new HashSet<long>();

            for (int x = 0; x < f.SizeX; x++)
            {
                for (int z = 0; z < f.SizeZ; z++)
                {
                    if (f.Layer[x, z] < 0) continue;

                    // x*c-ox 是格子中心，格子最小角要再减半格
                    float x0 = x * c - ox - c * 0.5f;
                    float x1 = x0 + c;
                    float z0 = z * c - oz - c * 0.5f;
                    float z1 = z0 + c;
                    int group = f.RegionGroup(x, z);

                    for (int d = 0; d < 4; d++)
                    {
                        if (!f.NeedWallAt(x, z, d)) continue;

                        // 该段栏杆的两个端点
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

                        GameObject prefab = kit.PickFence(group);
                        if (prefab == null) continue;

                        int yaw = (d % 2 == 0 ? 90 : 0) + kit.fenceYawOffset;
                        Vector3 mid;
                        switch (d)
                        {
                            case 0: mid = new Vector3((x0 + x1) * 0.5f, surface, z1); break;
                            case 1: mid = new Vector3(x1, surface, (z0 + z1) * 0.5f); break;
                            case 2: mid = new Vector3((x0 + x1) * 0.5f, surface, z0); break;
                            default: mid = new Vector3(x0, surface, (z0 + z1) * 0.5f); break;
                        }

                        var go = (GameObject)Object.Instantiate(prefab, container.transform);
                        go.name = "Fence_" + prefab.name;
                        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        go.transform.localPosition = mid;
                        count++;
                    }
                }
            }

            // 石桩：每段栏杆的两端各一个，重合的格角只放一个
            // 支撑：只放在已生成石桩的格角，竖直位置随机取地块上表面或下表面（原点为支撑顶部中心）
            var rng = new System.Random(s.seed ^ 0x2F6E2B1);
            for (int i = 0; i < verts.Count; i++)
            {
                Vector2Int v = verts[i];
                int group = vertGroups[i];
                var pos = new Vector3(v.x * c - ox - c * 0.5f, surface, v.y * c - oz - c * 0.5f);

                GameObject postPrefab = kit.PickPost(group);
                if (postPrefab == null) continue;

                var post = (GameObject)Object.Instantiate(postPrefab, container.transform);
                post.name = "Post_" + postPrefab.name;
                post.transform.localRotation = Quaternion.identity;
                post.transform.localPosition = pos;
                count++;

                GameObject supportPrefab = kit.PickSupport(group);
                if (supportPrefab == null) continue;

                var support = (GameObject)Object.Instantiate(supportPrefab, container.transform);
                support.name = "Support_" + supportPrefab.name;
                support.transform.localRotation = Quaternion.identity;
                support.transform.localPosition = new Vector3(pos.x, rng.Next(2) == 0 ? 0f : surface, pos.z);
                count++;
            }

            return count;
        }

        static void AddVert(List<Vector2Int> verts, List<int> groups, HashSet<long> seen, int cx, int cz, int group)
        {
            if (!seen.Add(((long)cx << 32) | (uint)cz)) return;
            verts.Add(new Vector2Int(cx, cz));
            groups.Add(group);
        }
    }
}
