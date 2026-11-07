using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 显隐控制器：自身不含任何子物体，只按自身位置决定 contentRoot 下哪些区块可见
    public class TerrainPropCuller : MonoBehaviour
    {
        [Tooltip("可见半径（米），以本物体为中心；建议比相机可视范围略大")]
        public float visibleDistance = 250f;

        readonly Dictionary<Vector2Int, Transform> chunkRoots = new Dictionary<Vector2Int, Transform>();
        readonly HashSet<int> activeIds = new HashSet<int>();
        readonly HashSet<int> nextIds = new HashSet<int>();

        ChunkSearcher<GameObject> index;
        Transform contentRoot;
        Transform outOfMapRoot;
        Vector3 lastPosition;
        bool dirty = true;
        bool warnedOutOfMap;

        public int InstanceCount { get; private set; }

        // 地形实例的挂载点（TerrainRoot），与本物体是兄弟节点，移动本物体不会带走地形
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

            contentRoot = content;
            index = new ChunkSearcher<GameObject>(go => go.transform.position);

            for (int i = 0; i < data.items.Count; i++)
            {
                TerrainItemData item = data.items[i];
                GameObject prefab = kit.GetPrefab(item.kind, item.prefabIndex);
                if (prefab == null) continue;

                Vector3 world = contentRoot.position + item.position;
                Transform parent = GetChunkRoot(world);
                var go = (GameObject)Object.Instantiate(prefab, parent);
                go.name = item.kind + "_" + prefab.name;
                go.transform.position = world;
                go.transform.rotation = Quaternion.Euler(0f, item.yaw, 0f);
                go.transform.localScale = Vector3.one * item.scale;
                InstanceCount++;
            }

            CreatePoints(data);

            // 初始全部视为可见，第一次同步把范围外的关掉（否则会漏掉"一开始就不在范围内"的区块）
            foreach (int id in index.Keys) activeIds.Add(id);

            lastPosition = transform.position;
            SyncVisibility();
        }

        void OnEnable()
        {
            dirty = true;
        }

        void LateUpdate()
        {
            if (index == null) return;
            if (!dirty && (transform.position - lastPosition).sqrMagnitude < 0.0001f) return;
            SyncVisibility();
        }

        void SyncVisibility()
        {
            lastPosition = transform.position;
            dirty = false;

            // 块中心在半径内不代表整块可见，多放一个块尺寸的余量
            index.GetIdsInRange(transform.position, visibleDistance + Landscape.chunkSize, nextIds);

            foreach (int id in nextIds)
            {
                if (activeIds.Contains(id)) continue;
                if (index.TryGetObject(id, out GameObject go) && go != null && !go.activeSelf) go.SetActive(true);
            }

            foreach (int id in activeIds)
            {
                if (nextIds.Contains(id)) continue;
                if (index.TryGetObject(id, out GameObject go) && go != null && go.activeSelf) go.SetActive(false);
            }

            activeIds.Clear();
            activeIds.UnionWith(nextIds);
        }

        Transform GetChunkRoot(Vector3 world)
        {
            Vector2Int chunk = ChunkSearcher<GameObject>.GetChunkIndex(world);
            if (chunk == ChunkSearcher<GameObject>.OutOfMapChunk)
            {
                if (!warnedOutOfMap)
                {
                    Debug.LogWarning("[地形] 有物件落在地图范围外，这些物件不参与区块剔除（始终显示）", this);
                    warnedOutOfMap = true;
                }
                if (outOfMapRoot == null)
                {
                    var go = new GameObject("OutOfMap");
                    go.transform.SetParent(contentRoot, false);
                    outOfMapRoot = go.transform;
                }
                return outOfMapRoot;
            }

            if (chunkRoots.TryGetValue(chunk, out Transform root)) return root;

            var chunkGo = new GameObject($"Chunk_{chunk.x}_{chunk.y}");
            chunkGo.transform.SetParent(contentRoot, false);
            chunkGo.transform.position = new Vector3(
                (chunk.x + 0.5f) * Landscape.chunkSize,
                contentRoot.position.y,
                (chunk.y + 0.5f) * Landscape.chunkSize);

            chunkRoots[chunk] = chunkGo.transform;
            index.Add(chunkGo.GetInstanceID(), chunkGo);
            return chunkGo.transform;
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
