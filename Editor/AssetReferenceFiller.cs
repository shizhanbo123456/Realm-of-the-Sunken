using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

// 资源字段批量填充：AssetReference 不支持多选拖拽，这里给每个资源字段加「填入选中 / 追加选中」按钮。
// 用法：Project 面板多选资源 → 在 Inspector 对应字段上点按钮；列表按资源名称自然序写入（15 号排在 100 号前）。
public abstract class AssetListInspectorBase : Editor
{
    private enum FillKind { None, AssetRefList, AssetRefSingle, ObjectList, ObjectSingle }

    private struct FillSpec
    {
        public FillKind kind;
        public Type elementType;   // AssetReferenceT<T> 的 T，或 List<T> 的 T
    }

    private struct FillRequest
    {
        public FieldInfo field;
        public FillSpec spec;
        public bool append;
        public string label;
    }

    private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> fieldCache = new();

    private FillRequest pending;
    private bool hasPending;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox("批量填充：Project 面板多选资源后，点字段右侧按钮。列表按资源名称自然序写入。", MessageType.Info);

        SerializedProperty property = serializedObject.GetIterator();
        bool enterChildren = true;
        while (property.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (property.propertyPath == "m_Script")
            {
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.PropertyField(property);
                continue;
            }

            FieldInfo field = FieldOf(property.propertyPath);
            FillSpec spec = field != null ? Classify(field.FieldType) : default(FillSpec);
            if (spec.kind == FillKind.None)
            {
                EditorGUILayout.PropertyField(property, true);
                continue;
            }

            DrawResourceRow(property, field, spec);
        }

        // 填充只能在遍历结束后做：中途 serializedObject.Update 会让迭代器失效
        serializedObject.ApplyModifiedProperties();

        if (!hasPending) return;
        hasPending = false;
        Apply(pending);
    }

    #region//Local
    private void DrawResourceRow(SerializedProperty property, FieldInfo field, FillSpec spec)
    {
        bool single = spec.kind == FillKind.AssetRefSingle || spec.kind == FillKind.ObjectSingle;

        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField(property.displayName, EditorStyles.boldLabel);
            if (GUILayout.Button(single ? "取选中首个" : "填入选中", EditorStyles.miniButton, GUILayout.Width(76f)))
                Queue(field, spec, false, property.displayName);
            if (!single)
            {
                if (GUILayout.Button("追加", EditorStyles.miniButton, GUILayout.Width(48f)))
                    Queue(field, spec, true, property.displayName);
            }
        }

        EditorGUILayout.PropertyField(property, GUIContent.none, true);
    }

    private void Queue(FieldInfo field, FillSpec spec, bool append, string label)
    {
        pending = new FillRequest { field = field, spec = spec, append = append, label = label };
        hasPending = true;
    }

    private void Apply(FillRequest request)
    {
        var picked = PickSelection(request.spec.elementType);
        if (picked.Count == 0)
        {
            Debug.LogWarning($"[资源填充] 当前选中项里没有 {request.spec.elementType.Name}：{request.label} 未改动");
            return;
        }

        WarnIfNotAddressable(request.spec, picked);
        if (targets.Length > 1)
            Debug.LogWarning($"[资源填充] 当前选中了 {targets.Length} 个对象，只填充第一个「{target.name}」");

        Undo.RecordObject(target, "填充资源引用");
        switch (request.spec.kind)
        {
            case FillKind.AssetRefList: SetReferenceList(request, picked); break;
            case FillKind.AssetRefSingle: SetReferenceSingle(request, picked); break;
            case FillKind.ObjectList: SetObjectList(request, picked); break;
            case FillKind.ObjectSingle: SetObjectSingle(request, picked); break;
        }
        EditorUtility.SetDirty(target);

        serializedObject.Update();
        Debug.Log($"[资源填充] {request.label} ← {picked.Count} 个（按名称自然序）");
    }

    private void SetReferenceList(FillRequest request, List<UnityEngine.Object> picked)
    {
        Type referenceType = typeof(AssetReferenceT<>).MakeGenericType(request.spec.elementType);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(referenceType));

        if (request.append)
        {
            var old = request.field.GetValue(target) as IList;
            if (old != null) foreach (var item in old) list.Add(item);
        }

        for (int i = 0; i < picked.Count; i++) list.Add(MakeReference(referenceType, picked[i]));
        request.field.SetValue(target, list);
    }

    private void SetReferenceSingle(FillRequest request, List<UnityEngine.Object> picked)
    {
        Type referenceType = typeof(AssetReferenceT<>).MakeGenericType(request.spec.elementType);
        request.field.SetValue(target, MakeReference(referenceType, picked[0]));
        if (picked.Count > 1)
            Debug.LogWarning($"[资源填充] {request.label} 是单值字段，只写了第一个「{picked[0].name}」");
    }

    private void SetObjectList(FillRequest request, List<UnityEngine.Object> picked)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(request.spec.elementType));

        if (request.append)
        {
            var old = request.field.GetValue(target) as IList;
            if (old != null) foreach (var item in old) list.Add(item);
        }

        for (int i = 0; i < picked.Count; i++) list.Add(picked[i]);
        request.field.SetValue(target, list);
    }

    private void SetObjectSingle(FillRequest request, List<UnityEngine.Object> picked)
    {
        request.field.SetValue(target, picked[0]);
        if (picked.Count > 1)
            Debug.LogWarning($"[资源填充] {request.label} 是单值字段，只写了第一个「{picked[0].name}」");
    }

    private static AssetReference MakeReference(Type referenceType, UnityEngine.Object asset)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException($"[资源填充] {asset.name} 不是磁盘资产，取不到 GUID");
        return (AssetReference)Activator.CreateInstance(referenceType, guid);
    }

    // Project 面板的选中项，只留与字段元素类型匹配的，按名称自然序（下标即资源编号时这条很关键）
    private static List<UnityEngine.Object> PickSelection(Type elementType)
    {
        var picked = new List<UnityEngine.Object>();
        var seen = new HashSet<int>();
        foreach (var asset in Selection.objects)
        {
            if (asset == null || !elementType.IsInstanceOfType(asset)) continue;
            if (!seen.Add(asset.GetInstanceID())) continue;
            picked.Add(asset);
        }
        picked.Sort((a, b) => EditorUtility.NaturalCompare(a.name, b.name));
        return picked;
    }

    private static void WarnIfNotAddressable(FillSpec spec, List<UnityEngine.Object> picked)
    {
        if (spec.kind != FillKind.AssetRefList && spec.kind != FillKind.AssetRefSingle) return;

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogWarning("[资源填充] Addressables 尚未初始化：引用已写入，但运行时加载会失败（先在 Addressables Groups 建一次配置）");
            return;
        }

        List<string> missing = null;
        for (int i = 0; i < picked.Count; i++)
        {
            string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked[i]));
            if (settings.FindAssetEntry(guid) != null) continue;
            if (missing == null) missing = new List<string>();
            missing.Add(picked[i].name);
        }
        if (missing != null)
            Debug.LogWarning($"[资源填充] 以下资产还不是 addressable，运行时加载会失败：{string.Join("、", missing)}");
    }

    private static FillSpec Classify(Type type)
    {
        if (type.IsGenericType)
        {
            if (type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = type.GetGenericArguments()[0];
                if (IsAssetReference(element, out Type assetType))
                    return new FillSpec { kind = FillKind.AssetRefList, elementType = assetType };
                if (typeof(UnityEngine.Object).IsAssignableFrom(element))
                    return new FillSpec { kind = FillKind.ObjectList, elementType = element };
                return default;
            }

            if (typeof(AssetReference).IsAssignableFrom(type))
                return new FillSpec { kind = FillKind.AssetRefSingle, elementType = type.GetGenericArguments()[0] };
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return new FillSpec { kind = FillKind.ObjectSingle, elementType = type };

        return default;
    }

    private static bool IsAssetReference(Type type, out Type assetType)
    {
        assetType = null;
        if (!type.IsGenericType || !typeof(AssetReference).IsAssignableFrom(type)) return false;
        assetType = type.GetGenericArguments()[0];
        return true;
    }

    private FieldInfo FieldOf(string path)
    {
        Type type = target.GetType();
        if (!fieldCache.TryGetValue(type, out var map))
        {
            map = new Dictionary<string, FieldInfo>();
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                map[field.Name] = field;
            fieldCache[type] = map;
        }
        return map.TryGetValue(path, out var found) ? found : null;
    }
    #endregion
}

[CustomEditor(typeof(ResourceManager))]
public class ResourceManagerEditor : AssetListInspectorBase { }

[CustomEditor(typeof(AssetsManager))]
public class AssetsManagerEditor : AssetListInspectorBase { }
