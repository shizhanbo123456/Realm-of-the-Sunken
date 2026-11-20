using System.IO;
using UnityEngine;

namespace TerrainGen
{
    // 地形底板图：每格有无地面 + 防御塔/守护点/中立雕像/攻守出生点格的专用色，烘焙时写到 Resources/Map，小地图三处共用
    public static class TerrainMapImage
    {
        public const string Folder = "Assets/Resources/Map";
        public const string AssetName = "terrain_map";
        public const string ResourcePath = "Map/" + AssetName;
        public const int Resolution = 1024;

        static Texture2D cached;
        static bool tried;

        // 每格按整数边界切方块，格子数变动也能铺满整张图；像素行序自下而上，正好对应世界 Z+
        // 点位所在的格整块盖成该类型专门的颜色，点位在小地图上一眼可见
        public static Texture2D Build(BuildingField field, Color floor, Color voidColor, Color towerColor,
            Color guardColor, Color statueColor, Color attackColor, Color defenseColor)
        {
            if (field == null || field.SizeX <= 0 || field.SizeZ <= 0) return null;

            var tex = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false);
            var pixels = new Color32[Resolution * Resolution];
            Color32 floorColor = floor;
            Color32 voidPx = voidColor;

            for (int x = 0; x < field.SizeX; x++)
            {
                for (int z = 0; z < field.SizeZ; z++)
                {
                    Color32 c = field.Layer[x, z] >= 0 ? floorColor : voidPx;
                    CellBounds(field, x, z, out int px0, out int px1, out int pz0, out int pz1);
                    Fill(pixels, px0, px1, pz0, pz1, c);
                }
            }

            Color32 towerPx = towerColor;
            Color32 guardPx = guardColor;
            Color32 statuePx = statueColor;
            Color32 attackPx = attackColor;
            Color32 defensePx = defenseColor;
            foreach (MapPoint p in field.Points)
            {
                Color32 c;
                switch (p.Kind)
                {
                    case PointKind.Tower: c = towerPx; break;
                    case PointKind.MainGuard:
                    case PointKind.SubGuard: c = guardPx; break;
                    case PointKind.NeutralStatue: c = statuePx; break;
                    case PointKind.AttackSpawn: c = attackPx; break;
                    case PointKind.DefenseSpawn: c = defensePx; break;
                    default: continue;
                }

                if (p.Cell.x < 0 || p.Cell.y < 0 || p.Cell.x >= field.SizeX || p.Cell.y >= field.SizeZ) continue;
                CellBounds(field, p.Cell.x, p.Cell.y, out int px0, out int px1, out int pz0, out int pz1);
                Fill(pixels, px0, px1, pz0, pz1, c);
            }

            tex.SetPixels32(pixels);
            tex.Apply();
            return tex;
        }

        static void CellBounds(BuildingField field, int x, int z,
            out int px0, out int px1, out int pz0, out int pz1)
        {
            px0 = x * Resolution / field.SizeX;
            px1 = (x + 1) * Resolution / field.SizeX;
            pz0 = z * Resolution / field.SizeZ;
            pz1 = (z + 1) * Resolution / field.SizeZ;
        }

        static void Fill(Color32[] pixels, int px0, int px1, int pz0, int pz1, Color32 c)
        {
            for (int pz = pz0; pz < pz1; pz++)
            {
                int row = pz * Resolution;
                for (int px = px0; px < px1; px++) pixels[row + px] = c;
            }
        }

        public static bool Write(BuildingField field, Color floor, Color voidColor, Color towerColor,
            Color guardColor, Color statueColor, Color attackColor, Color defenseColor, out string assetPath)
        {
            assetPath = Folder + "/" + AssetName + ".png";

            Texture2D tex = Build(field, floor, voidColor, towerColor, guardColor, statueColor, attackColor, defenseColor);
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
