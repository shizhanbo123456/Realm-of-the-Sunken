using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TerrainGen
{
    public enum TerrainItemKind : byte
    {
        Ground,
        Fence,
        PostShort,
        PostTall,
        Support,
        Building,
        Decoration,
        Prop,
        Bush,
        Vine,
        Obstacle,
        ObstacleDecor,
        Floating,
        Fish
    }

    public struct TerrainItemData
    {
        public TerrainItemKind kind;
        public int prefabIndex;   // 对应 BuildingKitSet 该类别列表里的下标
        public Vector3 position;  // 地形局部坐标（地形中心为原点）
        public float yaw;
        public float scale;
    }

    // 一次生成的完整结果：所有物件的摆放数据 + 战斗点位，二进制缓存到 StreamingAssets
    public class TerrainPlanData
    {
        const int Magic = 0x5447524E;
        const int FormatVersion = 1;

        public int seed;
        public int configVersion;
        public float cellSize = 6f;
        public float groundHeight = 4f;
        public int sizeX;
        public int sizeZ;

        public readonly List<TerrainItemData> items = new List<TerrainItemData>();
        public readonly List<MapPoint> points = new List<MapPoint>();

        public static string Folder => Path.Combine(Application.streamingAssetsPath, "TerrainGenerator");

        public static string PathFor(int seed, int configVersion)
        {
            return Path.Combine(Folder, $"terrain_{seed}_v{configVersion}.bin");
        }

        public bool Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
                using (var w = new BinaryWriter(stream))
                {
                    w.Write(Magic);
                    w.Write(FormatVersion);
                    w.Write(seed);
                    w.Write(configVersion);
                    w.Write(cellSize);
                    w.Write(groundHeight);
                    w.Write(sizeX);
                    w.Write(sizeZ);

                    w.Write(items.Count);
                    for (int i = 0; i < items.Count; i++)
                    {
                        TerrainItemData item = items[i];
                        w.Write((byte)item.kind);
                        w.Write(item.prefabIndex);
                        w.Write(item.position.x);
                        w.Write(item.position.y);
                        w.Write(item.position.z);
                        w.Write(item.yaw);
                        w.Write(item.scale);
                    }

                    w.Write(points.Count);
                    for (int i = 0; i < points.Count; i++)
                    {
                        MapPoint p = points[i];
                        w.Write(p.Cell.x);
                        w.Write(p.Cell.y);
                        w.Write((byte)p.Kind);
                    }
                }
                return true;
            }
            catch (IOException e)
            {
                Debug.LogError($"[地形] 写入失败 {path}：{e.Message}");
                return false;
            }
        }

        public static TerrainPlanData TryLoad(string path)
        {
            if (!File.Exists(path)) return null;

            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var r = new BinaryReader(stream))
                {
                    if (r.ReadInt32() != Magic)
                    {
                        Debug.LogError($"[地形] {path} 不是地形数据文件");
                        return null;
                    }

                    int format = r.ReadInt32();
                    if (format != FormatVersion)
                    {
                        Debug.LogWarning($"[地形] {path} 的数据格式版本为 {format}，当前为 {FormatVersion}，需要重新烘焙");
                        return null;
                    }

                    var data = new TerrainPlanData
                    {
                        seed = r.ReadInt32(),
                        configVersion = r.ReadInt32(),
                        cellSize = r.ReadSingle(),
                        groundHeight = r.ReadSingle(),
                        sizeX = r.ReadInt32(),
                        sizeZ = r.ReadInt32(),
                    };

                    int itemCount = r.ReadInt32();
                    for (int i = 0; i < itemCount; i++)
                    {
                        data.items.Add(new TerrainItemData
                        {
                            kind = (TerrainItemKind)r.ReadByte(),
                            prefabIndex = r.ReadInt32(),
                            position = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle()),
                            yaw = r.ReadSingle(),
                            scale = r.ReadSingle(),
                        });
                    }

                    int pointCount = r.ReadInt32();
                    for (int i = 0; i < pointCount; i++)
                    {
                        var cell = new Vector2Int(r.ReadInt32(), r.ReadInt32());
                        data.points.Add(new MapPoint(cell, (PointKind)r.ReadByte()));
                    }

                    return data;
                }
            }
            catch (IOException e)
            {
                Debug.LogError($"[地形] 读取失败 {path}：{e.Message}");
                return null;
            }
        }
    }
}
