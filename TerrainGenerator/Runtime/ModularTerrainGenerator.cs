using UnityEngine;

namespace TerrainGen
{
    public class ModularTerrainGenerator : MonoBehaviour
    {
        public BuildingSettings settings;

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

        // 客户端：素材表由 ResourceManager 装配，物件交给 Culler 按区块流式取还
        public TerrainPropCuller Generate(System.Action onGenerated = null)
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings", this);
                return null;
            }
            if (!Application.isPlaying)
            {
                Debug.LogError("[地形] 编辑器态请用 Tools/模块化地形/建筑生成器 烘焙数据，运行期入口不生成地形", this);
                onGenerated?.Invoke();
                return null;
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
                GenerateInternal(loaded);
                onGenerated?.Invoke();
            });
            return null;
        }

        private TerrainPropCuller GenerateInternal(BuildingKitSet kit)
        {
            Clear();
            culler = TerrainGenerator.Generate(settings, kit, out contentRoot);
            return culler;
        }

        // 服务器：素材表走 AssetsManager 的直接引用，一次性铺设整张地形，不建 Culler（服务器不做剔除）
        public void GenerateServer()
        {
            if (settings == null)
            {
                Debug.LogError("[地形] 未指定生成参数 BuildingSettings", this);
                return;
            }
            BuildingKitSet kit = Tool.AssetsManager != null ? Tool.AssetsManager.TerrainKit : null;
            if (kit == null)
            {
                Debug.LogError("[地形] AssetsManager 未配置 TerrainKit：服务器地形未生成", this);
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
