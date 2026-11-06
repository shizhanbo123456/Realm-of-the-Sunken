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

        enum Mode
        {
            BoxEnabled,   // 移除原有碰撞体，根物体加一个启用的 BoxCollider
            BoxDisabled,  // 同上但 enabled = false，只供生成器量尺寸，场景里不参与物理
            RemoveOnly    // 只移除碰撞体，不加任何碰撞体
        }

        struct Entry
        {
            public string name;
            public Func<BuildingKitSet, List<GameObject>> pick;
            public Mode mode;

            public Entry(string name, Func<BuildingKitSet, List<GameObject>> pick, Mode mode)
            {
                this.name = name;
                this.pick = pick;
                this.mode = mode;
            }
        }

        static readonly Entry[] Entries =
        {
            new Entry("地面", k => k.ground, Mode.BoxEnabled),
            new Entry("围栏", k => k.fences, Mode.BoxEnabled),
            new Entry("矮石柱", k => k.shortPosts, Mode.BoxEnabled),
            new Entry("高石柱", k => k.tallPosts, Mode.BoxEnabled),
            new Entry("支撑", k => k.supports, Mode.BoxEnabled),
            new Entry("建筑物", k => k.buildings, Mode.BoxEnabled),
            new Entry("装饰", k => k.decorations, Mode.BoxEnabled),
            new Entry("灌木", k => k.bushes, Mode.BoxDisabled),
            new Entry("摆件", k => k.props, Mode.BoxEnabled),
            new Entry("藤蔓", k => k.vines, Mode.RemoveOnly),
            new Entry("障碍", k => k.obstacles, Mode.BoxEnabled),
            new Entry("障碍装饰", k => k.obstacleDecorations, Mode.RemoveOnly),
            new Entry("漂浮装饰", k => k.floatingDecorations, Mode.RemoveOnly),
        };

        [MenuItem("Tools/模块化地形/碰撞体预处理/全部处理")]
        static void ProcessAll()
        {
            BuildingKitSet kit = LoadKit();
            if (kit == null) return;

            int done = 0;
            int skipped = 0;
            try
            {
                for (int i = 0; i < Entries.Length; i++)
                {
                    EditorUtility.DisplayProgressBar("碰撞体预处理", Entries[i].name, (i + 1f) / Entries.Length);
                    ProcessEntry(kit, Entries[i], ref done, ref skipped);
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[地形] 全部预处理完成：成功 {done} 个，跳过 {skipped} 个（原因见上方告警）");
        }

        [MenuItem("Tools/模块化地形/碰撞体预处理/地面")]
        static void ProcessGround() => RunOne("地面");

        [MenuItem("Tools/模块化地形/碰撞体预处理/围栏")]
        static void ProcessFences() => RunOne("围栏");

        [MenuItem("Tools/模块化地形/碰撞体预处理/矮石柱")]
        static void ProcessShortPosts() => RunOne("矮石柱");

        [MenuItem("Tools/模块化地形/碰撞体预处理/高石柱")]
        static void ProcessTallPosts() => RunOne("高石柱");

        [MenuItem("Tools/模块化地形/碰撞体预处理/支撑")]
        static void ProcessSupports() => RunOne("支撑");

        [MenuItem("Tools/模块化地形/碰撞体预处理/建筑物")]
        static void ProcessBuildings() => RunOne("建筑物");

        [MenuItem("Tools/模块化地形/碰撞体预处理/装饰")]
        static void ProcessDecorations() => RunOne("装饰");

        [MenuItem("Tools/模块化地形/碰撞体预处理/灌木")]
        static void ProcessBushes() => RunOne("灌木");

        [MenuItem("Tools/模块化地形/碰撞体预处理/摆件")]
        static void ProcessProps() => RunOne("摆件");

        [MenuItem("Tools/模块化地形/碰撞体预处理/藤蔓")]
        static void ProcessVines() => RunOne("藤蔓");

        [MenuItem("Tools/模块化地形/碰撞体预处理/障碍")]
        static void ProcessObstacles() => RunOne("障碍");

        [MenuItem("Tools/模块化地形/碰撞体预处理/障碍装饰")]
        static void ProcessObstacleDecorations() => RunOne("障碍装饰");

        [MenuItem("Tools/模块化地形/碰撞体预处理/漂浮装饰")]
        static void ProcessFloatingDecorations() => RunOne("漂浮装饰");

        static void RunOne(string name)
        {
            BuildingKitSet kit = LoadKit();
            if (kit == null) return;

            int done = 0;
            int skipped = 0;
            foreach (Entry e in Entries)
            {
                if (e.name != name) continue;
                ProcessEntry(kit, e, ref done, ref skipped);
                break;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[地形]「{name}」预处理完成：成功 {done} 个，跳过 {skipped} 个（原因见上方告警）");
        }

        static BuildingKitSet LoadKit()
        {
            var kit = AssetDatabase.LoadAssetAtPath<BuildingKitSet>(KitPath);
            if (kit == null) Debug.LogError($"[地形] 找不到素材表 {KitPath}");
            return kit;
        }

        static void ProcessEntry(BuildingKitSet kit, Entry entry, ref int done, ref int skipped)
        {
            List<GameObject> list = entry.pick(kit);
            if (list == null || list.Count == 0)
            {
                Debug.LogWarning($"[地形] 素材表「{entry.name}」列表为空，未做任何修改");
                return;
            }

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

                if (ProcessOne(path, entry.mode)) done++;
                else skipped++;
            }
        }

        static bool ProcessOne(string path, Mode mode)
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
                Bounds bounds = default;
                if (mode != Mode.RemoveOnly && !PrefabBounds.TryGetMeshBounds(root, out bounds))
                {
                    // 先量后改：没有 Mesh 就整体跳过，避免把原有碰撞体删了却没东西补
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

                if (mode != Mode.RemoveOnly)
                {
                    var box = root.GetComponent<BoxCollider>();
                    if (box == null) box = root.AddComponent<BoxCollider>();
                    box.center = bounds.center;
                    box.size = bounds.size;
                    box.enabled = mode == Mode.BoxEnabled;

                    if (box.size.x < 0.001f || box.size.y < 0.001f || box.size.z < 0.001f)
                        Debug.LogWarning($"[地形] {path} 计算出的包围盒有零尺寸轴 {box.size}，请检查该预制体");
                }

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
