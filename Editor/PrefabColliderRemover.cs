using UnityEditor;
using UnityEngine;

public static class PrefabColliderRemover
{
    private const string MenuPath = "预制体预处理/移除所有碰撞体";

    [MenuItem(MenuPath, false, 100)]
    private static void RemoveColliders()
    {
        var targets = Selection.gameObjects;
        if (targets == null || targets.Length == 0)
        {
            Debug.LogWarning("[预制体预处理] 请先在 Project / Hierarchy 中选中预制体");
            return;
        }

        int prefabs = 0, totalRemoved = 0;
        foreach (var go in targets)
        {
            if (go == null) continue;

            if (PrefabUtility.IsPartOfPrefabAsset(go))
            {
                totalRemoved += RemoveInAsset(go);
                prefabs++;
            }
            else if (PrefabUtility.IsPartOfPrefabInstance(go))
            {
                totalRemoved += RemoveInInstance(go);
                prefabs++;
            }
            else
            {
                Debug.LogWarning($"[预制体预处理] {go.name} 不是预制体，已跳过");
            }
        }

        if (prefabs > 0) AssetDatabase.SaveAssets();
        Debug.Log($"[预制体预处理] 处理 {prefabs} 个预制体，共移除 {totalRemoved} 个碰撞体");
    }

    // 预制体资产：在临时加载的预制体内容上删，再存回去（嵌套预制体的删除也能正确记录）
    private static int RemoveInAsset(GameObject root)
    {
        string path = AssetDatabase.GetAssetPath(root);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning($"[预制体预处理] 取不到 {root.name} 的资产路径，已跳过");
            return 0;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(path);
        try
        {
            int removed = RemoveCollidersIn(contents, false);
            if (removed > 0) PrefabUtility.SaveAsPrefabAsset(contents, path);
            return removed;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    // 场景里的预制体实例：逐个子物体删，走 Undo，删除会记成预制体覆盖
    private static int RemoveInInstance(GameObject root)
    {
        return RemoveCollidersIn(root, true);
    }

    private static int RemoveCollidersIn(GameObject root, bool useUndo)
    {
        var colliders = root.GetComponentsInChildren<Collider>(true); // true = 含未激活的子物体
        int removed = 0;
        foreach (var col in colliders)
        {
            if (col == null) continue;
            if (useUndo) Undo.DestroyObjectImmediate(col);
            else Object.DestroyImmediate(col, true);
            removed++;
        }
        return removed;
    }
}
