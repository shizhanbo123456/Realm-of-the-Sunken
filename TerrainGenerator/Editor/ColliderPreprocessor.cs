using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.EditorTools
{
    // 只维护素材预制体根上的一个“尺寸盒”（禁用的 BoxCollider），生成期靠它 O(1) 量尺寸；
    // 预制体里其它碰撞体（手摆的激活碰撞体）一律不碰
    public static class ColliderPreprocessor
    {
        const string KitPath = "Assets/Scripts/TerrainGenerator/Config/BuildingKitSet.asset";

        enum Mode
        {
            BoxDisabled,  // 根物体写一个禁用 BoxCollider，只供生成器量尺寸，场景里不参与物理
            NoBounds      // 不加尺寸盒，预制体完全不动
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
            new Entry("地面", k => k.ground, Mode.BoxDisabled),
            new Entry("围栏", k => k.fences, Mode.BoxDisabled),
            new Entry("矮石柱", k => k.shortPosts, Mode.BoxDisabled),
            new Entry("高石柱", k => k.tallPosts, Mode.BoxDisabled),
            new Entry("支撑", k => k.supports, Mode.BoxDisabled),
            new Entry("建筑物", k => k.buildings, Mode.BoxDisabled),
            new Entry("装饰", k => k.decorations, Mode.BoxDisabled),
            new Entry("灌木", k => k.bushes, Mode.BoxDisabled),
            new Entry("摆件", k => k.props, Mode.BoxDisabled),
            new Entry("藤蔓", k => k.vines, Mode.NoBounds),
            new Entry("障碍", k => k.obstacles, Mode.BoxDisabled),
            new Entry("障碍装饰", k => k.obstacleDecorations, Mode.NoBounds),
            new Entry("漂浮装饰", k => k.floatingDecorations, Mode.NoBounds),
            new Entry("鱼群", k => k.fishSchools, Mode.NoBounds),
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

        [MenuItem("Tools/模块化地形/碰撞体预处理/鱼群")]
        static void ProcessFishSchools() => RunOne("鱼群");

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
            if (mode == Mode.NoBounds) return true; // 这类不加尺寸盒，也不动预制体

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null)
            {
                Debug.LogError($"[地形] 无法打开预制体 {path}");
                return false;
            }

            bool ok = false;
            try
            {
                if (!PrefabBounds.TryGetMeshBounds(root, out Bounds bounds))
                {
                    Debug.LogWarning($"[地形] {path} 下没有任何 Mesh，跳过（预制体未做改动）");
                    return false;
                }

                // 只维护根上这一个尺寸盒，永远禁用；预制体里其它碰撞体（手摆的激活碰撞体）一律不碰
                var box = root.GetComponent<BoxCollider>();
                if (box == null) box = root.AddComponent<BoxCollider>();
                box.center = bounds.center;

                // 零厚度轴（贴片类素材）给个极小厚度，避免出现无效尺寸
                Vector3 size = bounds.size;
                if (size.x <= 0f) size.x = 0.01f;
                if (size.y <= 0f) size.y = 0.01f;
                if (size.z <= 0f) size.z = 0.01f;
                box.size = size;
                box.enabled = false;

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
