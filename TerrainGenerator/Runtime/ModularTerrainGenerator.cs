using UnityEngine;

namespace TerrainGen
{
    public class ModularTerrainGenerator : MonoBehaviour
    {
        public BuildingSettings settings;
        [Tooltip("地面/围栏/石柱/支撑/植被摆件素材表")]
        public BuildingKitSet kit;
        [Tooltip("生成到哪个节点下，留空则生成到自身")]
        public Transform container;
        public bool generateOnStart;

        TerrainPropCuller culler;

        void Start()
        {
            if (generateOnStart) Generate();
        }

        // 编辑器非播放态：只把数据烘焙到 StreamingAssets/TerrainGenerator 并返回 null
        // 播放态：优先读缓存数据，缺失则运行期生成，返回挂好数据的 Culler
        public TerrainPropCuller Generate()
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings", this);
                return null;
            }

            Clear();
            culler = TerrainGenerator.Generate(settings, kit, container != null ? container : transform);
            return culler;
        }

        public void Clear()
        {
            // Culler 在场景根上、内容根挂在 container 下，两处都要清；靠 Culler 上记录的 ContentRoot 找到内容
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
