using UnityEngine;

namespace TerrainGen
{
    public class ModularTerrainGenerator : MonoBehaviour
    {
        public BuildingSettings settings;
        [Tooltip("地面/围栏/石柱/支撑/植被摆件素材表")]
        public BuildingKitSet kit;

        TerrainPropCuller culler;
        Transform contentRoot;

        void Awake()
        {
            Tool.ModularTerrainGenerator = this;
        }

        // 服务器启动即常驻整张地形：只放有启用碰撞体的类别，且除地面外只留碰撞体，供地面射线与点位使用
        void Start()
        {
            if (Tool.Instance == null)
            {
                Debug.LogError("[地形] 场景缺少 Tool，无法判定服务器/客户端：地形未生成", this);
                return;
            }
            if (Tool.Instance.isServer) GenerateServer();
        }

        // 客户端：建内容根与点位，物件交给 Culler 按区块流式取还
        // 编辑器非播放态：只把数据烘焙到 StreamingAssets/TerrainGenerator 并返回 null
        public TerrainPropCuller Generate(System.Action onGenerated = null)
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings", this);
                return null;
            }

            // 编辑器烘焙直接用 Inspector 里的 kit
            if (!Application.isPlaying)
            {
                TerrainPropCuller baked = GenerateInternal();
                onGenerated?.Invoke();
                return baked;
            }

            // 运行时客户端由 ResourceManager 加载素材表：kit 上直接引用会让预制体被重复打包，必须留空
            if (kit != null)
            {
                Debug.LogError("[地形] 客户端场景不应直接挂 kit（地形预制体会被重复打包），请清空它并在 ResourceManager 配好 BuildingKit", this);
                kit = null;
            }
            if (Tool.ResourceManager == null)
            {
                Debug.LogError("[地形] 客户端缺少 ResourceManager，无法加载地形素材表", this);
                return null;
            }

            Tool.ResourceManager.LoadBuildingKit(loaded =>
            {
                if (loaded == null)
                {
                    Debug.LogError("[地形] 地形素材表加载失败，地形未生成", this);
                    onGenerated?.Invoke();
                    return;
                }
                kit = loaded;
                GenerateInternal();
                onGenerated?.Invoke();
            });
            return null;
        }

        private TerrainPropCuller GenerateInternal()
        {
            Clear();
            culler = TerrainGenerator.Generate(settings, kit, out contentRoot);
            return culler;
        }

        // 服务器：一次性铺设整张地形，不建 Culler（服务器不做剔除）
        public void GenerateServer()
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings", this);
                return;
            }
            if (kit == null)
            {
                Debug.LogError("[地形] 服务器场景未指定素材表 kit：地形未生成", this);
                return;
            }

            Clear();
            TerrainGenerator.GenerateServer(settings, kit, out contentRoot);
        }

        public void Clear()
        {
            TerrainSpawns.Reset();

            // 客户端的内容根记在 Culler 上，服务器没有 Culler，记在自己身上
            Transform content = contentRoot;
            contentRoot = null;

            TerrainPropCuller[] found = FindObjectsOfType<TerrainPropCuller>();
            for (int i = 0; i < found.Length; i++)
            {
                if (found[i] == null) continue;
                if (content == null) content = found[i].ContentRoot;
                DestroyNode(found[i].gameObject);
            }

            if (content != null) DestroyNode(content.gameObject);
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
