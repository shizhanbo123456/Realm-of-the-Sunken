using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 显隐控制器：自身不含任何子物体；按间隔、且仅在位置变化时重新判定区块显隐
    public class TerrainPropCuller : MonoBehaviour
    {
        [Tooltip("可见半径（米），以本物体为中心；建议比相机可视范围略大")]
        public float visibleDistance = 50f;

        [Tooltip("检测间隔（秒）：隔这么久才判定一次，0 = 每帧判定")]
        public float checkInterval = 0.25f;

        [Tooltip("静默阈值（米）：位置移动小于该值视为静止，本轮不做任何判定")]
        public float quietDistance = 1f;

        readonly List<GameObject> chunks = new List<GameObject>();
        readonly Dictionary<Vector2Int, int> chunkIds = new Dictionary<Vector2Int, int>();

        Transform contentRoot;
        Transform outOfMapRoot;
        bool warnedOutOfMap;
        float timer;
        Vector3 lastCheckPos;
        float lastRadius = float.NaN;

        public int InstanceCount { get; private set; }

        // 地形实例的挂载点（TerrainRoot），与本物体无父子关系，移动本物体不会带走地形
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

            if (chunks.Count == 0)
            {
                Debug.LogError($"[地形] 没有任何物件落入区块：地图边长 {Landscape.MapSize}，区块数 {Landscape.chunkCount}，"
                               + $"内容根 {contentRoot.position}，剔除无法生效", this);
            }
            else if (!AnyChunkOutside(transform.position))
            {
                Debug.LogWarning($"[地形] 判定半径 {visibleDistance + Landscape.chunkSize:0}m 覆盖了全部 {chunks.Count} 个区块，"
                                 + $"当前不会隐藏任何区块（visibleDistance = {visibleDistance}），需要调小半径或移动本物体", this);
            }

            lastCheckPos = transform.position;
            lastRadius = visibleDistance;
            SyncVisibility(transform.position);
        }

        // 按间隔判定；未到间隔、或位置没动且半径没变时直接 return，不做任何计算
        void LateUpdate()
        {
            if (chunks.Count == 0) return;

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

        // 逐块比对目标状态，不做增量记录，避免状态不同步导致"只开不关"
        void SyncVisibility(Vector3 center)
        {
            float radiusSq = RadiusSquared;

            for (int i = 0; i < chunks.Count; i++)
            {
                GameObject chunk = chunks[i];
                if (chunk == null) continue;

                Vector3 pos = chunk.transform.position;
                float dx = pos.x - center.x;
                float dz = pos.z - center.z;
                bool visible = dx * dx + dz * dz <= radiusSq;
                if (chunk.activeSelf != visible) chunk.SetActive(visible);
            }
        }

        bool AnyChunkOutside(Vector3 center)
        {
            float radiusSq = RadiusSquared;

            for (int i = 0; i < chunks.Count; i++)
            {
                GameObject chunk = chunks[i];
                if (chunk == null) continue;

                Vector3 pos = chunk.transform.position;
                float dx = pos.x - center.x;
                float dz = pos.z - center.z;
                if (dx * dx + dz * dz > radiusSq) return true;
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

            if (chunkIds.TryGetValue(chunk, out int index)) return chunks[index].transform;

            var chunkGo = new GameObject($"Chunk_{chunk.x}_{chunk.y}");
            chunkGo.transform.SetParent(contentRoot, false);
            chunkGo.transform.position = new Vector3(
                (chunk.x + 0.5f) * Landscape.chunkSize,
                contentRoot.position.y,
                (chunk.y + 0.5f) * Landscape.chunkSize);

            chunkIds[chunk] = chunks.Count;
            chunks.Add(chunkGo);
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
