using UnityEngine;

namespace TerrainGen
{
    public class ModularTerrainGenerator : MonoBehaviour
    {
        public BuildingSettings settings;
        [Tooltip("地面/围栏/石柱/支撑/植被摆件素材表")]
        public BuildingKitSet kit;

        TerrainPropCuller culler;

        void Awake()
        {
            Tool.ModularTerrainGenerator = this;
        }

        // 服务器启动即常驻整张地形：只放有启用碰撞体的类别，地面射线与点位才覆盖全图
        void Start()
        {
            if (Tool.Instance == null)
            {
                Debug.LogError("[地形] 场景缺少 Tool，无法判定服务器/客户端：地形未生成", this);
                return;
            }
            if (Tool.Instance.isServer) Generate(false, true);
        }

        // 编辑器非播放态：只把数据烘焙到 StreamingAssets/TerrainGenerator 并返回 null
        // 播放态：优先读缓存数据，缺失则运行期生成，返回挂好数据的 Culler
        public TerrainPropCuller Generate(bool culled = true, bool colliderOnly = false)
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings", this);
                return null;
            }

            Clear();
            culler = TerrainGenerator.Generate(settings, kit, culled, colliderOnly);
            return culler;
        }

        public void Clear()
        {
            TerrainSpawns.Reset();

            // Culler 与内容根都是场景根物体，两处都要清；靠 Culler 上记录的 ContentRoot 找到内容
            TerrainPropCuller[] found = FindObjectsOfType<TerrainPropCuller>();
            for (int i = 0; i < found.Length; i++)
            {
                TerrainPropCuller item = found[i];
                if (item == null) continue;
                Transform content = item.ContentRoot;
                DestroyNode(item.gameObject);
                if (content != null) DestroyNode(content.gameObject);
            }
            culler = null;
        }

        void DestroyNode(GameObject go)
        {
            if (Application.isPlaying) Destroy(go);
            else DestroyImmediate(go);
        }

        public TerrainPropCuller Culler => culler;
    }
}
