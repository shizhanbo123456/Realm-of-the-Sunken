using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 显隐控制器：自身不含子物体；区块进入视野时从池里取物件，离开时归还，全程不销毁
    public class TerrainPropCuller : MonoBehaviour
    {
        [Tooltip("可见半径（米），以主相机为中心；建议比相机可视范围略大")]
        public float visibleDistance = 50f;

        [Tooltip("false = 不剔除，全部区块一次常驻（服务器用）")]
        public bool culled = true;

        [Tooltip("true = 只放有启用碰撞体的类别，藤蔓/障碍装饰/漂浮装饰/鱼群/灌木全部跳过（服务器用）")]
        public bool colliderOnly;

        [Tooltip("true = 除地面外只留根物体与碰撞体：子物体全销毁、根上 MeshRenderer 全移除（服务器用）")]
        public bool stripToCollider;

        [Tooltip("检测间隔（秒）：隔这么久才判定一次，0 = 每帧判定")]
        public float checkInterval = 0.25f;

        [Tooltip("静默阈值（米）：位置移动小于该值视为静止，本轮不做任何判定")]
        public float quietDistance = 1f;

        struct ItemDef
        {
            public TerrainItemKind kind;
            public int prefabIndex;
            public Vector3 position;   // 相对内容根
            public float yaw;
            public float scale;
        }

        readonly Dictionary<Vector2Int, List<ItemDef>> chunkDefs = new Dictionary<Vector2Int, List<ItemDef>>();
        readonly Dictionary<Vector2Int, List<GameObject>> liveChunks = new Dictionary<Vector2Int, List<GameObject>>();
        readonly Dictionary<Vector2Int, Transform> chunkRoots = new Dictionary<Vector2Int, Transform>();
        readonly Dictionary<GameObject, Stack<GameObject>> free = new Dictionary<GameObject, Stack<GameObject>>();
        readonly Dictionary<GameObject, GameObject> instancePrefabs = new Dictionary<GameObject, GameObject>();

        BuildingKitSet kit;
        Transform contentRoot;
        Transform poolRoot;
        float timer;
        Vector3 lastCheckPos;
        float lastRadius = float.NaN;

        // 已创建过的实例总数（在场景里的 + 池里的）
        public int InstanceCount { get; private set; }

        public Transform ContentRoot => contentRoot;

        public void Initialize(TerrainPlanData data, BuildingKitSet kit, Transform content)
        {
            if (data == null)
            {
                Debug.LogError("[地形] Culler 初始化失败：数据为空", this);
                return;
            }
            if (kit == null)
            {
                Debug.LogError("[地形] Culler 初始化失败：未指定素材表", this);
                return;
            }
            if (content == null)
            {
                Debug.LogError("[地形] Culler 初始化失败：未指定内容根节点", this);
                return;
            }

            this.kit = kit;
            contentRoot = content;

            if (ConfigManager.GlobalVariableConfig.terrain_chunk_size <= 0f
                || ConfigManager.GlobalVariableConfig.terrain_chunk_size_count <= 0)
            {
                Debug.LogError("[地形] Culler 初始化失败：区块尺寸/数量未从配表读到", this);
                return;
            }

            var pool = new GameObject("Pool");
            pool.transform.SetParent(contentRoot, false);
            poolRoot = pool.transform;

            var colliderCache = new Dictionary<GameObject, bool>();
            for (int i = 0; i < data.items.Count; i++)
            {
                TerrainItemData item = data.items[i];
                if (!KeepItem(item, colliderCache)) continue;

                Vector3 world = contentRoot.position + item.position;
                Vector2Int chunk = ChunkSearcher<GameObject>.GetChunkIndex(world);
                // 贴边物件会因浮点误差落到图外，夹到最近的区块，跟随该区块一起显隐
                if (chunk == ChunkSearcher<GameObject>.OutOfMapChunk) chunk = NearestChunk(world);

                if (!chunkDefs.TryGetValue(chunk, out List<ItemDef> list))
                {
                    list = new List<ItemDef>();
                    chunkDefs[chunk] = list;
                }

                list.Add(new ItemDef
                {
                    kind = item.kind,
                    prefabIndex = item.prefabIndex,
                    position = item.position,
                    yaw = item.yaw,
                    scale = item.scale,
                });
            }

            CreatePoints(data);

            if (chunkDefs.Count == 0)
            {
                Debug.LogError("[地形] 没有任何区块被创建，剔除无法生效", this);
                return;
            }

            if (!culled)
            {
                LoadAll();
                return;
            }

            Vector3 cameraPos = Camera.main.transform.position;
            if (!AnyChunkOutside(cameraPos))
            {
                Debug.LogWarning($"[地形] 判定半径 {visibleDistance + ConfigManager.GlobalVariableConfig.terrain_chunk_size:0}m 以主相机位置为心覆盖了全部 {chunkDefs.Count} 个区块，"
                                 + $"当前不会回收任何区块（visibleDistance = {visibleDistance}），需要调小半径", this);
            }

            lastCheckPos = cameraPos;
            lastRadius = visibleDistance;
            SyncVisibility(cameraPos);
        }

        // 按预制体自身的碰撞体判定，碰撞体预处理改了模式也不用同步改这里
        bool KeepItem(TerrainItemData item, Dictionary<GameObject, bool> cache)
        {
            if (!colliderOnly) return true;

            GameObject prefab = kit.GetPrefab(item.kind, item.prefabIndex);
            if (prefab == null) return false;
            if (!cache.TryGetValue(prefab, out bool active))
            {
                active = HasActiveCollider(prefab);
                cache[prefab] = active;
            }
            return active;
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

        // 按间隔判定；未到间隔、或相机没动且半径没变时直接 return，不做任何计算
        void LateUpdate()
        {
            if (!culled || chunkDefs.Count == 0) return;

            timer += Time.unscaledDeltaTime;
            if (timer < checkInterval) return;
            timer = 0f;

            Vector3 pos = Camera.main.transform.position;
            if ((pos - lastCheckPos).sqrMagnitude < quietDistance * quietDistance
                && Mathf.Approximately(visibleDistance, lastRadius)) return;

            lastCheckPos = pos;
            lastRadius = visibleDistance;
            SyncVisibility(pos);
        }

        void LoadAll()
        {
            foreach (KeyValuePair<Vector2Int, List<ItemDef>> pair in chunkDefs) LoadChunk(pair.Key, pair.Value);
        }

        // 只处理状态翻转的区块：进视野整块取用，出视野整块归还
        void SyncVisibility(Vector3 center)
        {
            float radiusSq = RadiusSquared;

            foreach (KeyValuePair<Vector2Int, List<ItemDef>> pair in chunkDefs)
            {
                Vector2Int key = pair.Key;
                bool visible = ChunkDistanceSq(key, center) <= radiusSq;
                bool loaded = liveChunks.ContainsKey(key);
                if (visible == loaded) continue;

                if (visible) LoadChunk(key, pair.Value);
                else UnloadChunk(key);
            }
        }

        void LoadChunk(Vector2Int key, List<ItemDef> defs)
        {
            Transform root = GetChunkRoot(key);
            var list = new List<GameObject>(defs.Count);
            Vector3 rootPos = contentRoot.position;

            for (int i = 0; i < defs.Count; i++)
            {
                ItemDef def = defs[i];
                GameObject prefab = kit.GetPrefab(def.kind, def.prefabIndex);
                if (prefab == null) continue;

                GameObject go = Take(prefab, root, def.kind);
                go.name = def.kind + "_" + prefab.name;
                go.transform.position = rootPos + def.position;
                go.transform.rotation = Quaternion.Euler(0f, def.yaw, 0f);
                go.transform.localScale = Vector3.one * def.scale;
                go.SetActive(true);
                list.Add(go);
            }

            liveChunks[key] = list;
        }

        void UnloadChunk(Vector2Int key)
        {
            if (!liveChunks.TryGetValue(key, out List<GameObject> list)) return;
            for (int i = 0; i < list.Count; i++) Return(list[i]);
            liveChunks.Remove(key);
        }

        GameObject Take(GameObject prefab, Transform parent, TerrainItemKind kind)
        {
            if (free.TryGetValue(prefab, out Stack<GameObject> stack) && stack.Count > 0)
            {
                GameObject reused = stack.Pop();
                reused.transform.SetParent(parent, false);
                return reused;
            }

            GameObject created = Instantiate(prefab, parent);
            created.SetActive(false);
            instancePrefabs[created] = prefab;
            InstanceCount++;
            // 池化实例只精简一次：碰撞体在根物体上（见 ColliderPreprocessor），销毁子物体不影响物理
            if (stripToCollider && kind != TerrainItemKind.Ground) StripInstance(created);
            return created;
        }

        void StripInstance(GameObject go)
        {
            // 先关再销毁：Destroy 延迟到帧末，不关的话当帧还会跟着 SetActive(true) 渲染一次
            for (int i = go.transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = go.transform.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
            foreach (MeshRenderer renderer in go.GetComponents<MeshRenderer>())
            {
                Destroy(renderer);
            }
        }

        void Return(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            go.transform.SetParent(poolRoot, false);
            if (!instancePrefabs.TryGetValue(go, out GameObject prefab)) return;

            if (!free.TryGetValue(prefab, out Stack<GameObject> stack))
            {
                stack = new Stack<GameObject>();
                free[prefab] = stack;
            }
            stack.Push(go);
        }

        Transform GetChunkRoot(Vector2Int chunk)
        {
            if (chunkRoots.TryGetValue(chunk, out Transform root) && root != null) return root;

            var go = new GameObject($"Chunk_{chunk.x}_{chunk.y}");
            go.transform.SetParent(contentRoot, false);
            float mapMin = ConfigManager.GlobalVariableConfig.terrain_size_min;
            float chunkSize = ConfigManager.GlobalVariableConfig.terrain_chunk_size;
            go.transform.position = new Vector3(
                mapMin + (chunk.x + 0.5f) * chunkSize,
                contentRoot.position.y,
                mapMin + (chunk.y + 0.5f) * chunkSize);

            chunkRoots[chunk] = go.transform;
            return go.transform;
        }

        // 图外坐标按最近区块夹取：越界的轴取端点，未越界的轴保留原索引
        Vector2Int NearestChunk(Vector3 world)
        {
            float mapMin = ConfigManager.GlobalVariableConfig.terrain_size_min;
            float chunkSize = ConfigManager.GlobalVariableConfig.terrain_chunk_size;
            int x = Mathf.FloorToInt((world.x - mapMin) / chunkSize);
            int z = Mathf.FloorToInt((world.z - mapMin) / chunkSize);
            int last = ConfigManager.GlobalVariableConfig.terrain_chunk_size_count - 1;
            return new Vector2Int(Mathf.Clamp(x, 0, last), Mathf.Clamp(z, 0, last));
        }

        float ChunkDistanceSq(Vector2Int chunk, Vector3 center)
        {
            float mapMin = ConfigManager.GlobalVariableConfig.terrain_size_min;
            float chunkSize = ConfigManager.GlobalVariableConfig.terrain_chunk_size;
            float cx = mapMin + (chunk.x + 0.5f) * chunkSize;
            float cz = mapMin + (chunk.y + 0.5f) * chunkSize;
            float dx = cx - center.x;
            float dz = cz - center.z;
            return dx * dx + dz * dz;
        }

        bool AnyChunkOutside(Vector3 center)
        {
            float radiusSq = RadiusSquared;
            foreach (Vector2Int chunk in chunkDefs.Keys)
            {
                if (ChunkDistanceSq(chunk, center) > radiusSq) return true;
            }
            return false;
        }

        float RadiusSquared
        {
            get
            {
                float radius = visibleDistance + ConfigManager.GlobalVariableConfig.terrain_chunk_size;
                return radius * radius;
            }
        }

        void CreatePoints(TerrainPlanData data)
        {
            if (data.points.Count == 0) return;

            var container = new GameObject("Points");
            container.transform.SetParent(contentRoot, false);

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
