using UnityEngine;

using Ros.Config;
namespace TerrainGen
{

    public static class TerrainAssembler
    {
        public const string RootName = "TerrainRoot";
        public const string CullerName = "TerrainCuller";

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
                cellSize = TerrainGeneration.F(terrain_generationEnum.cell_size),
                groundHeight = TerrainGeneration.F(terrain_generationEnum.ground_height),
                sizeX = field.SizeX,
                sizeZ = field.SizeZ,
            };

            if (kit != null)
            {
                BuildingDecorator.BuildPlan(field, kit, data.items);
                PropPlacer.PlacePlan(field, kit, data.items, s.seed);
            }

            data.points.AddRange(field.Points);
            Debug.Log($"[地形] 摆放数据完成：{data.items.Count} 个物件、{data.points.Count} 个点位；"
                      + "尺寸只读取根物体 BoxCollider（O(1)），未做任何网格计算");
            return data;
        }

        // 服务器一次性铺设：只铺有激活碰撞体的类别（地面无条件铺，它是底板）；stripMeshes 时除地面外只留碰撞体
        // 烘焙导航时传 stripMeshes = false：类别过滤不变，但保留完整网格，NavMesh 才按真实几何生成
        public static void SpawnColliderOnly(TerrainPlanData data, BuildingKitSet kit, Transform root, bool stripMeshes = true)
        {
            int placed = 0;
            int stripped = 0;
            for (int i = 0; i < data.items.Count; i++)
            {
                TerrainItemData item = data.items[i];
                GameObject prefab = kit.GetPrefab(item.kind, item.prefabIndex);
                if (prefab == null) continue;
                if (item.kind != TerrainItemKind.Ground && !HasActiveCollider(prefab)) continue;

                var go = Object.Instantiate(prefab, root);
                go.name = item.kind + "_" + prefab.name;
                go.transform.position = root.position + item.position;
                go.transform.rotation = Quaternion.Euler(0f, item.yaw, 0f);
                go.transform.localScale = item.scale;

                if (stripMeshes && item.kind != TerrainItemKind.Ground)
                {
                    StripVisuals(go.transform);
                    stripped++;
                }
                placed++;
            }

            if (!stripMeshes)
            {
                Debug.Log($"[地形] 铺设完成（保留网格，供烘焙导航）：{placed} 个物件");
                return;
            }
            Debug.Log($"[地形] 服务器铺设完成：{placed} 个物件（其中 {stripped} 个只保留根碰撞体，其余为地面模型）");
        }

        // 有真正参与物理的碰撞体（根上那个禁用的尺寸盒不算），决定服务器铺设与 NavMesh 烘焙是否包含该物件
        static bool HasActiveCollider(GameObject prefab)
        {
            Collider[] cols = prefab.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].enabled) return true;
            }
            return false;
        }

        // 服务器只留碰撞：删掉所有 MeshRenderer，砍掉不含任何碰撞体的子树，带碰撞体的子树原样保留
        static void StripVisuals(Transform node)
        {
            for (int i = node.childCount - 1; i >= 0; i--)
            {
                Transform child = node.GetChild(i);
                if (child.GetComponentsInChildren<Collider>(true).Length == 0)
                {
                    // 先关再销毁：Destroy 延迟到帧末，不关的话当帧还会渲染一次
                    child.gameObject.SetActive(false);
                    Kill(child.gameObject);
                    continue;
                }

                StripVisuals(child);
            }

            foreach (MeshRenderer renderer in node.GetComponents<MeshRenderer>())
            {
                Kill(renderer);
            }
        }

        // 编辑器里烘焙导航时会走同一套铺设，Destroy 在非播放态不生效
        static void Kill(Object target)
        {
            if (Application.isPlaying) Object.Destroy(target);
            else Object.DestroyImmediate(target);
        }

        // 点位标记：空物体 + Gizmo，客户端与服务器都要，供调试与战斗逻辑读坐标
        public static void CreatePoints(TerrainPlanData data, Transform root)
        {
            if (data.points.Count == 0) return;

            var container = new GameObject("Points");
            container.transform.SetParent(root, false);

            // 与摆放数据同一口径：0 号格中心的地图坐标
            float mapMin = GlobalVariable.RangeMin(global_variablesEnum.terrain_size);
            float ox = mapMin + data.cellSize * 0.5f;
            float oz = mapMin + data.cellSize * 0.5f;
            int subIndex = 0;
            int statueIndex = 0;
            int towerIndex = 0;

            for (int i = 0; i < data.points.Count; i++)
            {
                MapPoint p = data.points[i];
                string label;
                Color color;
                switch (p.Kind)
                {
                    case PointKind.MainGuard:
                        label = "Guard_Main";
                        color = new Color(1f, 0.35f, 0.15f);
                        break;
                    case PointKind.SubGuard:
                        label = "Guard_Sub_" + (++subIndex);
                        color = new Color(1f, 0.75f, 0.2f);
                        break;
                    case PointKind.NeutralStatue:
                        label = "Statue_" + (++statueIndex);
                        color = new Color(0.72f, 0.45f, 1f);
                        break;
                    case PointKind.Tower:
                        label = "Tower_" + (++towerIndex);
                        color = new Color(0.35f, 0.95f, 0.45f);
                        break;
                    default:
                        Debug.LogError($"[地形] 点位 ({p.Cell.x},{p.Cell.y}) 的类型 {(int)p.Kind} 未定义：烘焙数据已损坏，需要重新烘焙");
                        label = "Unknown";
                        color = Color.magenta;
                        break;
                }

                var go = new GameObject(label);
                go.transform.SetParent(container.transform, false);
                go.transform.localPosition = new Vector3(
                    p.Cell.x * data.cellSize + ox,
                    data.groundHeight,
                    p.Cell.y * data.cellSize + oz);

                var marker = go.AddComponent<PointMarker>();
                marker.label = label;
                marker.color = color;
                marker.radius = data.cellSize * (p.Kind == PointKind.Tower ? 0.45f : 0.6f);
            }
        }
    }
}
