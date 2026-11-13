using System.IO;
using UnityEditor;
using UnityEngine;

public static class SlashWaveWrapperBuilder
{
    private const string SourceFolder = "Assets/Packages/VFX/Piloto Studio/Slashes/Prefab";
    private const string TargetFolder = "Assets/Files/Prefabs/VFX/Slashes";
    private const string MenuPath = "Tools/特效/生成斩击包装预制体";

    private static readonly Vector3 ChildPosition = new Vector3(0f, 0f, 1f);
    private static readonly Vector3 ChildRotation = new Vector3(0f, 180f, 0f);
    private static readonly Vector3 ChildScale = new Vector3(0.5f, 0.5f, 0.5f);

    [MenuItem(MenuPath, false, 31)]
    private static void Build()
    {
        if (!AssetDatabase.IsValidFolder(SourceFolder))
        {
            EditorUtility.DisplayDialog("斩击包装", $"源目录不存在：\n{SourceFolder}", "知道了");
            return;
        }
        EnsureFolder(TargetFolder);

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SourceFolder });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("斩击包装", "源目录下没有预制体。", "知道了");
            return;
        }

        int created = 0, skipped = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string sourcePath = AssetDatabase.GUIDToAssetPath(guids[i]);
                string targetPath = $"{TargetFolder}/{Path.GetFileName(sourcePath)}";
                EditorUtility.DisplayProgressBar("斩击包装", targetPath, (float)i / guids.Length);

                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(targetPath)))
                {
                    skipped++;
                    continue;
                }
                if (BuildOne(sourcePath, targetPath)) created++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[斩击包装] 完成：新建 {created} 个，跳过已存在 {skipped} 个，共扫描 {guids.Length} 个");
    }

    [MenuItem(MenuPath, true)]
    private static bool BuildValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

    // 根节点保持 identity，源预制体以嵌套实例的形式挂到其下
    private static bool BuildOne(string sourcePath, string targetPath)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
        if (source == null)
        {
            Debug.LogError($"[斩击包装] 加载失败：{sourcePath}");
            return false;
        }

        var root = new GameObject(source.name);
        try
        {
            var child = (GameObject)PrefabUtility.InstantiatePrefab(source);
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = ChildPosition;
            child.transform.localEulerAngles = ChildRotation;
            child.transform.localScale = ChildScale;

            PrefabUtility.SaveAsPrefabAsset(root, targetPath, out bool success);
            if (!success) Debug.LogError($"[斩击包装] 保存失败：{targetPath}");
            return success;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(parent)) return;
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
