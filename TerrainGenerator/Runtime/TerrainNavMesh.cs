using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

namespace TerrainGen
{
    // 地形导航：编辑器烘出来的 NavMeshData 放在 Resources，运行时挂 NavMeshSurface 注入
    public static class TerrainNavMesh
    {
        public const string Folder = "Assets/Resources/Nav";
        public const string AssetName = "NavMesh-Terrain";
        public const string ResourcePath = "Nav/" + AssetName;

        public static void Attach(Transform contentRoot)
        {
            NavMeshData data = Resources.Load<NavMeshData>(ResourcePath);
            if (data == null)
            {
                Debug.LogError($"[地形] 缺少导航数据 Resources/{ResourcePath}.asset："
                               + "请先在地形生成器面板点「烘焙NavMesh」，否则所有寻路都会失败");
                return;
            }

            var go = new GameObject("TerrainNavMesh");
            go.transform.SetParent(contentRoot, false);

            // AddComponent 时 OnEnable 先跑了一次（那时还没数据），所以设完数据要再显式 AddData
            var surface = go.AddComponent<NavMeshSurface>();
            surface.navMeshData = data;
            surface.AddData();
        }
    }
}
