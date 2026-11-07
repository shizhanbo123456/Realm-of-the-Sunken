using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 显隐控制器：自身不含子物体；区块进入视野时从池里取物件，离开时归还，全程不销毁
    public class TerrainPropCuller : MonoBehaviour
    {
        [Tooltip("可见半径（米），以本物体为中心；建议比相机可视范围略大")]
        public float visibleDistance = 50f;

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

        static readonly Vector2Int OutOfMapKey = new Vector2Int(int.MinValue, int.MinValue);

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

            var pool = new GameObject("Pool");
            pool.transform.SetParent(contentRoot, false);
            poolRoot = pool.transform;

            bool warnedOutOfMap = false;
            for (int i = 0; i < data.items.Count; i++)
            {
                TerrainItemData item = data.items[i];
                Vector2Int chunk = ChunkSearcher<GameObject>.GetChunkIndex(contentRoot.position + item.position);
                if (chunk == ChunkSearcher<GameObject>.OutOfMapChunk)
                {
                    if (!warnedOutOfMap)
                    {
                        Debug.LogWarning("[地形] 有物件落在地图范围外，这些物件常驻显示", this);
                        warnedOutOfMap = true;
                    }
                    chunk = OutOfMapKey;
                }

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
            }
            else if (!AnyChunkOutside(transform.position))
            {
                Debug.LogWarning($"[地形] 判定半径 {visibleDistance + Landscape.chunkSize:0}m 覆盖了全部 {chunkDefs.Count} 个区块，"
                                 + $"当前不会回收任何区块（visibleDistance = {visibleDistance}），需要调小半径或移动本物体", this);
            }

            lastCheckPos = transform.position;
            lastRadius = visibleDistance;
            SyncVisibility(transform.position);
        }

        // 按间隔判定；未到间隔、或位置没动且半径没变时直接 return，不做任何计算
        void LateUpdate()
        {
            if (chunkDefs.Count == 0) return;

            timer += Time.unscaledDeltaTime;
            if (timer < checkInterval) return;
            timer = 0f;

            Vector3 pos = transform.position;
            if ((pos - lastCheckPos).sqrMagnitude < quietDistance * quietDistance
                && Mathf.Approximately(visibleDistance, lastRadius)) return;

            lastCheckPos = pos;
            lastRadius = visibleDistance;
            SyncVisibility(pos);
        }

        // 只处理状态翻转的区块：进视野整块取用，出视野整块归还
        void SyncVisibility(Vector3 center)
        {
            float radiusSq = RadiusSquared;

            foreach (KeyValuePair<Vector2Int, List<ItemDef>> pair in chunkDefs)
            {
                Vector2Int key = pair.Key;
                bool visible = key == OutOfMapKey || ChunkDistanceSq(key, center) <= radiusSq;
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

                GameObject go = Take(prefab, root);
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

        GameObject Take(GameObject prefab, Transform parent)
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
            return created;
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

            var go = new GameObject(chunk == OutOfMapKey ? "OutOfMap" : $"Chunk_{chunk.x}_{chunk.y}");
            go.transform.SetParent(contentRoot, false);
            go.transform.position = chunk == OutOfMapKey
                ? contentRoot.position
                : new Vector3(
                    (chunk.x + 0.5f) * Landscape.chunkSize,
                    contentRoot.position.y,
                    (chunk.y + 0.5f) * Landscape.chunkSize);

            chunkRoots[chunk] = go.transform;
            return go.transform;
        }

        float ChunkDistanceSq(Vector2Int chunk, Vector3 center)
        {
            float cx = (chunk.x + 0.5f) * Landscape.chunkSize;
            float cz = (chunk.y + 0.5f) * Landscape.chunkSize;
            float dx = cx - center.x;
            float dz = cz - center.z;
            return dx * dx + dz * dz;
        }

        bool AnyChunkOutside(Vector3 center)
        {
            float radiusSq = RadiusSquared;
            foreach (Vector2Int chunk in chunkDefs.Keys)
            {
                if (chunk == OutOfMapKey) continue;
                if (ChunkDistanceSq(chunk, center) > radiusSq) return true;
            }
            return false;
        }

        float RadiusSquared
        {
            get
            {
                float radius = visibleDistance + Landscape.chunkSize;
                return radius * radius;
            }
        }

        void CreatePoints(TerrainPlanData data)
        {
            if (data.points.Count == 0) return;

            var container = new GameObject("Points");
            container.transform.SetParent(contentRoot, false);

            float ox = (data.sizeX - 1) * 0.5f * data.cellSize;
            float oz = (data.sizeZ - 1) * 0.5f * data.cellSize;
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
                    p.Cell.x * data.cellSize - ox,
                    data.groundHeight,
                    p.Cell.y * data.cellSize - oz);

                var marker = go.AddComponent<PointMarker>();
                marker.label = label;
                marker.color = color;
                marker.radius = data.cellSize * (p.Kind == PointKind.Tower ? 0.45f : 0.6f);
            }
        }
    }
}
