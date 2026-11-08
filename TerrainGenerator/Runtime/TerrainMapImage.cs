using System.IO;
using UnityEngine;

namespace TerrainGen
{
    // 地形底板图：只表达每格有无地面，烘焙时写到 Resources/Map，小地图三处共用
    public static class TerrainMapImage
    {
        public const string Folder = "Assets/Resources/Map";
        public const string AssetName = "terrain_map";
        public const string ResourcePath = "Map/" + AssetName;
        public const int Resolution = 1024;

        static Texture2D cached;
        static bool tried;

        // 每格按整数边界切方块，格子数变动也能铺满整张图；像素行序自下而上，正好对应世界 Z+
        public static Texture2D Build(BuildingField field, Color floor, Color voidColor)
        {
            if (field == null || field.SizeX <= 0 || field.SizeZ <= 0) return null;

            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            var pixels = new Color32[Resolution * Resolution];
            Color32 floorColor = floor;
            Color32 voidPx = voidColor;

            for (int x = 0; x < field.SizeX; x++)
            {
                int px0 = x * Resolution / field.SizeX;
                int px1 = (x + 1) * Resolution / field.SizeX;
                for (int z = 0; z < field.SizeZ; z++)
                {
                    Color32 c = field.Layer[x, z] >= 0 ? floorColor : voidPx;
                    int pz0 = z * Resolution / field.SizeZ;
                    int pz1 = (z + 1) * Resolution / field.SizeZ;
                    for (int pz = pz0; pz < pz1; pz++)
                    {
                        int row = pz * Resolution;
                        for (int px = px0; px < px1; px++) pixels[row + px] = c;
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        public static bool Write(BuildingField field, Color floor, Color voidColor, out string assetPath)
        {
            assetPath = Folder + "/" + AssetName + ".png";

            Texture2D tex = Build(field, floor, voidColor);
            if (tex == null)
            {
                Debug.LogError("[地形] 底图生成失败：布局为空");
                return false;
            }

            byte[] png = tex.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(tex);
            if (png == null)
            {
                Debug.LogError("[地形] 底图编码 PNG 失败");
                return false;
            }

            string dir = Path.Combine(Application.dataPath, "Resources", "Map");
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, AssetName + ".png"), png);
            return true;
        }

        // 缺失只在首次报错：三处小地图每帧都会走到这里
        public static Texture2D Load()
        {
            if (tried) return cached;
            tried = true;
            cached = Resources.Load<Texture2D>(ResourcePath);
            if (cached == null)
                Debug.LogError($"[地形] 缺少底图 Resources/{ResourcePath}.png：先在地形生成器面板点「烘焙数据」");
            return cached;
        }
    }
}
