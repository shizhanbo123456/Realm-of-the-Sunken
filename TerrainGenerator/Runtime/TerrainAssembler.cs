using UnityEngine;

namespace TerrainGen
{
    using Gen = ConfigManager.TerrainGenerationConfig;

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
                cellSize = Gen.cell_size,
                groundHeight = Gen.ground_height,
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

        // 服务器一次性铺设：只铺有启用碰撞体的类别；stripMeshes 时除地面外只留根物体与根碰撞体
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
                if (!HasActiveCollider(prefab)) continue;

                var go = Object.Instantiate(prefab, root);
                go.name = item.kind + "_" + prefab.name;
                go.transform.position = root.position + item.position;
                go.transform.rotation = Quaternion.Euler(0f, item.yaw, 0f);
                go.transform.localScale = Vector3.one * item.scale;

                if (stripMeshes && item.kind != TerrainItemKind.Ground)
                {
                    StripToCollider(go);
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

        static bool HasActiveCollider(GameObject prefab)
        {
            Collider[] cols = prefab.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                if (cols[i].enabled) return true;
            }
            return false;
        }

        // 先关子物体再销毁：Destroy 延迟到帧末，不关的话当帧还会渲染一次根上的模型
        static void StripToCollider(GameObject go)
        {
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Kill(child);
            }
            foreach (MeshRenderer renderer in go.GetComponents<MeshRenderer>())
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
            float mapMin = ConfigManager.GlobalVariableConfig.terrain_size_min;
            float ox = mapMin + data.cellSize * 0.5f;
            float oz = mapMin + data.cellSize * 0.5f;
            int subIndex = 0;
            int extraIndex = 0;
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
                    case PointKind.Tower:
                        label = "Tower_" + (++towerIndex);
                        color = new Color(0.35f, 0.95f, 0.45f);
                        break;
                    default:
                        label = "Open_" + (++extraIndex);
                        color = new Color(0.25f, 0.8f, 1f);
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
