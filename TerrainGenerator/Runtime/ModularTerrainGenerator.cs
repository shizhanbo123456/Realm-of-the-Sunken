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
            Transform parent = container != null ? container : transform;
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (child.GetComponent<TerrainPropCuller>() == null) continue;
                if (Application.isPlaying) Destroy(child.gameObject);
                else DestroyImmediate(child.gameObject);
            }
            culler = null;
        }

        public TerrainPropCuller Culler => culler;
    }
}
