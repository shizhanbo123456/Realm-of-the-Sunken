using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.EditorTools
{
    // 把素材表里每类预制体统一成"根物体一个 BoxCollider"，生成器就不必再回退到 Mesh 测量
    public static class ColliderPreprocessor
    {
        const string KitPath = "Assets/Scripts/TerrainGenerator/Config/BuildingKitSet.asset";

        [MenuItem("Tools/模块化地形/碰撞体预处理/地面")]
        static void ProcessGround() => Run(k => k.ground, "地面");

        [MenuItem("Tools/模块化地形/碰撞体预处理/围栏")]
        static void ProcessFences() => Run(k => k.fences, "围栏");

        [MenuItem("Tools/模块化地形/碰撞体预处理/矮石柱")]
        static void ProcessShortPosts() => Run(k => k.shortPosts, "矮石柱");

        [MenuItem("Tools/模块化地形/碰撞体预处理/高石柱")]
        static void ProcessTallPosts() => Run(k => k.tallPosts, "高石柱");

        [MenuItem("Tools/模块化地形/碰撞体预处理/支撑")]
        static void ProcessSupports() => Run(k => k.supports, "支撑");

        [MenuItem("Tools/模块化地形/碰撞体预处理/建筑物")]
        static void ProcessBuildings() => Run(k => k.buildings, "建筑物");

        [MenuItem("Tools/模块化地形/碰撞体预处理/装饰")]
        static void ProcessDecorations() => Run(k => k.decorations, "装饰");

        [MenuItem("Tools/模块化地形/碰撞体预处理/灌木")]
        static void ProcessBushes() => Run(k => k.bushes, "灌木");

        [MenuItem("Tools/模块化地形/碰撞体预处理/摆件")]
        static void ProcessProps() => Run(k => k.props, "摆件");

        [MenuItem("Tools/模块化地形/碰撞体预处理/藤蔓")]
        static void ProcessVines() => Run(k => k.vines, "藤蔓");

        static void Run(Func<BuildingKitSet, List<GameObject>> select, string category)
        {
            var kit = AssetDatabase.LoadAssetAtPath<BuildingKitSet>(KitPath);
            if (kit == null)
            {
                Debug.LogError($"[地形] 找不到素材表 {KitPath}");
                return;
            }

            List<GameObject> list = select(kit);
            if (list == null || list.Count == 0)
            {
                Debug.LogWarning($"[地形] 素材表「{category}」列表为空，未做任何修改");
                return;
            }

            int done = 0;
            int skipped = 0;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    GameObject prefab = list[i];
                    if (prefab == null)
                    {
                        skipped++;
                        continue;
                    }

                    string path = AssetDatabase.GetAssetPath(prefab);
                    if (string.IsNullOrEmpty(path))
                    {
                        Debug.LogWarning($"[地形] {prefab.name} 不是预制体资产，跳过");
                        skipped++;
                        continue;
                    }

                    EditorUtility.DisplayProgressBar("碰撞体预处理", $"{category}：{prefab.name}", (i + 1f) / list.Count);
                    if (ProcessOne(path)) done++;
                    else skipped++;
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[地形]「{category}」预处理完成：成功 {done} 个，跳过 {skipped} 个（原因见上方告警）");
        }

        static bool ProcessOne(string path)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogError($"[地形] 无法打开预制体 {path}");
                return false;
            }

            bool ok = false;
            try
            {
                // 先量后改：没有 Mesh 就整体跳过，避免把原有 Collider 删了却没东西补
                if (!PrefabBounds.TryGetMeshBounds(root, out Bounds bounds))
                {
                    Debug.LogWarning($"[地形] {path} 下没有任何 Mesh，跳过（预制体未做改动）");
                    return false;
                }

                foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
                {
                    if (PrefabUtility.IsPartOfPrefabInstance(col))
                    {
                        Debug.LogWarning($"[地形] {path} 的 {col.GetType().Name} 属于嵌套预制体实例，无法移除，请到源预制体处理");
                        continue;
                    }
                    UnityEngine.Object.DestroyImmediate(col);
                }

                var box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;

                if (box.size.x < 0.001f || box.size.y < 0.001f || box.size.z < 0.001f)
                    Debug.LogWarning($"[地形] {path} 计算出的包围盒有零尺寸轴 {box.size}，请检查该预制体");

                if (PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                {
                    Debug.LogError($"[地形] 保存预制体失败 {path}");
                    return false;
                }
                ok = true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return ok;
        }
    }
}
