using UnityEditor;
using UnityEngine;

// 把整张地图「每一列最高表面高度」烘成一张 R16 高度图，供全屏焦散做「上方是否被挡住」判定
public static class CausticsOcclusionBaker
{
    public const int Resolution = 2048;
    public const string AssetPath = "Assets/Resources/Caustics/CausticsOcclusion.asset";
    public const string ResourcePath = "Caustics/CausticsOcclusion";

    const string FolderPath = "Assets/Resources/Caustics";
    const string ParentFolder = "Assets/Resources";

    // root 必须是 SpawnColliderOnly(stripMeshes: false) 铺出来的完整地形
    public static bool Bake(Transform root, float groundHeight, out string message)
    {
        float mapMin = TerrainSpawns.MapMin;
        float mapSize = TerrainSpawns.MapSize;
        if (mapSize <= 0f)
        {
            message = "烘焙失败：配表 terrain_size 无效（<=0），无法确定世界范围";
            return false;
        }

        float cellsPerMeter = Resolution / mapSize;

        // 先铺满地面高度：没有物件的列（虚空、空地）按地面算，等于不遮挡
        var heights = new float[Resolution * Resolution];
        for (int i = 0; i < heights.Length; i++) heights[i] = groundHeight;

        MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(true);
        float highest = groundHeight;

        for (int r = 0; r < renderers.Length; r++)
        {
            Bounds bounds = renderers[r].bounds;
            int x0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.x - mapMin) * cellsPerMeter), 0, Resolution - 1);
            int x1 = Mathf.Clamp(Mathf.FloorToInt((bounds.max.x - mapMin) * cellsPerMeter), 0, Resolution - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt((bounds.min.z - mapMin) * cellsPerMeter), 0, Resolution - 1);
            int z1 = Mathf.Clamp(Mathf.FloorToInt((bounds.max.z - mapMin) * cellsPerMeter), 0, Resolution - 1);

            float top = bounds.max.y;
            if (top > highest) highest = top;

            // 行号对应世界 Z，列号对应世界 X，与 shader 里 (xz - MapMin) / MapSize 的取值一致
            for (int z = z0; z <= z1; z++)
            {
                int row = z * Resolution;
                for (int x = x0; x <= x1; x++)
                {
                    if (top > heights[row + x]) heights[row + x] = top;
                }
            }
        }

        var half = new ushort[heights.Length];
        for (int i = 0; i < heights.Length; i++) half[i] = Mathf.FloatToHalf(heights[i]);

        // R16 存世界高度（半浮点），不做 0~1 归一化，省掉高度范围这一层配置
        var texture = new Texture2D(Resolution, Resolution, TextureFormat.R16, false, true)
        {
            name = "CausticsOcclusion",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };
        texture.SetPixelData(half, 0);
        texture.Apply(false, false);

        if (!AssetDatabase.IsValidFolder(FolderPath))
        {
            if (!AssetDatabase.IsValidFolder(ParentFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.CreateFolder(ParentFolder, "Caustics");
        }

        AssetDatabase.DeleteAsset(AssetPath);
        AssetDatabase.CreateAsset(texture, AssetPath);
        AssetDatabase.SaveAssets();

        message = $"焦散遮挡图已烘焙：{Resolution}×{Resolution}，覆盖 {mapSize:0.#}m，最高面 {highest:0.##}m，参与物件 {renderers.Length} 个";
        return true;
    }
}
