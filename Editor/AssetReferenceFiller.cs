using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;
using UnityEngine.AddressableAssets;

// 资源字段批量填充：AssetReference 不支持多选拖拽，这里按 Project 面板的选中项一次性生成整串引用。
// 两个入口：工具窗口 Tools/资源/资源字段批量填充，以及 Inspector 字段标题行的按钮。
public static class ResourceFieldFiller
{
    public enum Kind { None, AssetRefList, AssetRefSingle, ObjectList, ObjectSingle }

    public struct Spec
    {
        public Kind kind;
        public Type elementType;   // AssetReferenceT<T> 的 T，或 List<T> 的 T
    }

    private static readonly Dictionary<Type, FieldInfo[]> fieldCache = new();

    public static FieldInfo[] ResourceFields(Type type)
    {
        if (fieldCache.TryGetValue(type, out var cached)) return cached;

        var fields = new List<FieldInfo>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (Classify(field.FieldType).kind == Kind.None) continue;
            fields.Add(field);
        }
        var result = fields.ToArray();
        fieldCache[type] = result;
        return result;
    }

    public static Spec Classify(Type type)
    {
        if (type.IsGenericType)
        {
            if (type.GetGenericTypeDefinition() == typeof(List<>))
            {
                Type element = type.GetGenericArguments()[0];
                if (IsAssetReference(element, out Type assetType))
                    return new Spec { kind = Kind.AssetRefList, elementType = assetType };
                if (typeof(UnityEngine.Object).IsAssignableFrom(element))
                    return new Spec { kind = Kind.ObjectList, elementType = element };
                return default(Spec);
            }

            if (typeof(AssetReference).IsAssignableFrom(type))
                return new Spec { kind = Kind.AssetRefSingle, elementType = type.GetGenericArguments()[0] };
        }

        if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            return new Spec { kind = Kind.ObjectSingle, elementType = type };

        return default(Spec);
    }

    // Project 面板的选中项，只留与字段元素类型匹配的，按名称自然序（下标即资源编号时这条很关键）
    public static List<UnityEngine.Object> PickSelection(Type elementType)
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

    public static string DescribeCount(UnityEngine.Object target, FieldInfo field)
    {
        object value = field.GetValue(target);
        if (value is IList list) return $"{list.Count} 项";
        return value != null ? "已填" : "空";
    }

    // 返回写入数量；0 表示选中项里没有匹配的类型，目标未改动
    public static int Fill(UnityEngine.Object target, FieldInfo field, bool append)
    {
        Spec spec = Classify(field.FieldType);
        var picked = PickSelection(spec.elementType);
        if (picked.Count == 0)
        {
            Debug.LogWarning($"[资源填充] 当前选中项里没有 {spec.elementType.Name}：{field.Name} 未改动");
            return 0;
        }

        WarnIfNotAddressable(spec, picked);
        Undo.RecordObject(target, "填充资源引用");

        switch (spec.kind)
        {
            case Kind.AssetRefList: SetReferenceList(target, field, spec, picked, append); break;
            case Kind.AssetRefSingle: SetReferenceSingle(target, field, spec, picked); break;
            case Kind.ObjectList: SetObjectList(target, field, spec, picked, append); break;
            case Kind.ObjectSingle: SetObjectSingle(target, field, spec, picked); break;
        }

        EditorUtility.SetDirty(target);
        Debug.Log($"[资源填充] {field.Name} ← {picked.Count} 个（按名称自然序）");
        return picked.Count;
    }

    public static void Clear(UnityEngine.Object target, FieldInfo field)
    {
        Spec spec = Classify(field.FieldType);
        Undo.RecordObject(target, "清空资源引用");

        if (spec.kind == Kind.AssetRefList)
        {
            Type referenceType = typeof(AssetReferenceT<>).MakeGenericType(spec.elementType);
            field.SetValue(target, Activator.CreateInstance(typeof(List<>).MakeGenericType(referenceType)));
        }
        else if (spec.kind == Kind.ObjectList)
        {
            field.SetValue(target, Activator.CreateInstance(typeof(List<>).MakeGenericType(spec.elementType)));
        }
        else
        {
            field.SetValue(target, null);
        }

        EditorUtility.SetDirty(target);
        Debug.Log($"[资源填充] {field.Name} 已清空");
    }

    #region//Local
    private static void SetReferenceList(UnityEngine.Object target, FieldInfo field, Spec spec,
        List<UnityEngine.Object> picked, bool append)
    {
        Type referenceType = typeof(AssetReferenceT<>).MakeGenericType(spec.elementType);
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(referenceType));

        if (append)
        {
            var old = field.GetValue(target) as IList;
            if (old != null) foreach (var item in old) list.Add(item);
        }

        for (int i = 0; i < picked.Count; i++) list.Add(MakeReference(referenceType, picked[i]));
        field.SetValue(target, list);
    }

    private static void SetReferenceSingle(UnityEngine.Object target, FieldInfo field, Spec spec,
        List<UnityEngine.Object> picked)
    {
        Type referenceType = typeof(AssetReferenceT<>).MakeGenericType(spec.elementType);
        field.SetValue(target, MakeReference(referenceType, picked[0]));
        if (picked.Count > 1)
            Debug.LogWarning($"[资源填充] {field.Name} 是单值字段，只写了第一个「{picked[0].name}」");
    }

    private static void SetObjectList(UnityEngine.Object target, FieldInfo field, Spec spec,
        List<UnityEngine.Object> picked, bool append)
    {
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(spec.elementType));

        if (append)
        {
            var old = field.GetValue(target) as IList;
            if (old != null) foreach (var item in old) list.Add(item);
        }

        for (int i = 0; i < picked.Count; i++) list.Add(picked[i]);
        field.SetValue(target, list);
    }

    private static void SetObjectSingle(UnityEngine.Object target, FieldInfo field, Spec spec,
        List<UnityEngine.Object> picked)
    {
        field.SetValue(target, picked[0]);
        if (picked.Count > 1)
            Debug.LogWarning($"[资源填充] {field.Name} 是单值字段，只写了第一个「{picked[0].name}」");
    }

    private static AssetReference MakeReference(Type referenceType, UnityEngine.Object asset)
    {
        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));
        if (string.IsNullOrEmpty(guid))
            throw new InvalidOperationException($"[资源填充] {asset.name} 不是磁盘资产，取不到 GUID");
        return (AssetReference)Activator.CreateInstance(referenceType, guid);
    }

    private static bool IsAssetReference(Type type, out Type assetType)
    {
        assetType = null;
        if (!type.IsGenericType || !typeof(AssetReference).IsAssignableFrom(type)) return false;
        assetType = type.GetGenericArguments()[0];
        return true;
    }

    private static void WarnIfNotAddressable(Spec spec, List<UnityEngine.Object> picked)
    {
        if (spec.kind != Kind.AssetRefList && spec.kind != Kind.AssetRefSingle) return;

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
    #endregion
}

// 工具窗口：目标组件由自己查找（不依赖 Inspector 选择），每个资源字段一行按钮
public class ResourceFieldFillWindow : EditorWindow
{
    private static readonly string[] Sides = { "客户端 ResourceManager", "服务器 AssetsManager" };

    private int side;
    private UnityEngine.Object target;
    private Vector2 scroll;

    [MenuItem("Tools/资源/资源字段批量填充")]
    private static void Open()
    {
        GetWindow<ResourceFieldFillWindow>("资源字段填充").Show();
    }

    private void OnEnable()
    {
        Reload();
    }

    private void Reload()
    {
        target = side == 0
            ? (UnityEngine.Object)FindObjectOfType<ResourceManager>()
            : FindObjectOfType<AssetsManager>();
    }

    private void OnGUI()
    {
        int pickedSide = GUILayout.Toolbar(side, Sides);
        if (pickedSide != side)
        {
            side = pickedSide;
            Reload();
        }

        if (target == null)
        {
            EditorGUILayout.HelpBox($"当前已加载的场景里没有{Sides[side]} 组件。", MessageType.Warning);
            if (GUILayout.Button("重新查找")) Reload();
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button($"定位：{target.name}", GUILayout.Width(220f))) EditorGUIUtility.PingObject(target);
            if (GUILayout.Button("重新查找", GUILayout.Width(80f))) Reload();
        }

        EditorGUILayout.LabelField($"Project 面板已选 {Selection.objects.Length} 个资源", EditorStyles.miniLabel);

        scroll = EditorGUILayout.BeginScrollView(scroll);
        foreach (var field in ResourceFieldFiller.ResourceFields(target.GetType()))
            DrawRow(field);
        EditorGUILayout.EndScrollView();
    }

    private void DrawRow(FieldInfo field)
    {
        ResourceFieldFiller.Spec spec = ResourceFieldFiller.Classify(field.FieldType);
        bool single = spec.kind == ResourceFieldFiller.Kind.AssetRefSingle
                      || spec.kind == ResourceFieldFiller.Kind.ObjectSingle;
        int picked = ResourceFieldFiller.PickSelection(spec.elementType).Count;

        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.Label(ObjectNames.NicifyVariableName(field.Name), GUILayout.Width(210f));
            GUILayout.Label(ResourceFieldFiller.DescribeCount(target, field), EditorStyles.miniLabel, GUILayout.Width(52f));

            using (new EditorGUI.DisabledScope(picked == 0))
            {
                if (GUILayout.Button(single ? "取选中首个" : $"填入选中({picked})", GUILayout.Width(96f)))
                {
                    ResourceFieldFiller.Fill(target, field, false);
                    Repaint();
                }
            }

            if (single) return;

            if (GUILayout.Button("追加", GUILayout.Width(48f)))
            {
                ResourceFieldFiller.Fill(target, field, true);
                Repaint();
            }
            if (GUILayout.Button("清空", GUILayout.Width(48f)))
            {
                ResourceFieldFiller.Clear(target, field);
                Repaint();
            }
        }
    }
}

// Inspector 入口：字段标题行右侧的按钮（窗口入口失效时仍可用这个）
public abstract class AssetListInspectorBase : Editor
{
    private FieldInfo pendingField;
    private bool pendingAppend;
    private bool hasPending;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.HelpBox("批量填充：Project 面板多选资源后点字段右侧按钮；也可用 Tools/资源/资源字段批量填充", MessageType.Info);

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

            FieldInfo field = FindField(property.propertyPath);
            if (field == null)
            {
                EditorGUILayout.PropertyField(property, true);
                continue;
            }

            ResourceFieldFiller.Spec spec = ResourceFieldFiller.Classify(field.FieldType);
            bool single = spec.kind == ResourceFieldFiller.Kind.AssetRefSingle
                          || spec.kind == ResourceFieldFiller.Kind.ObjectSingle;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(property.displayName, EditorStyles.boldLabel);
                if (GUILayout.Button(single ? "取选中首个" : "填入选中", EditorStyles.miniButton, GUILayout.Width(76f)))
                {
                    pendingField = field;
                    pendingAppend = false;
                    hasPending = true;
                }
                if (!single && GUILayout.Button("追加", EditorStyles.miniButton, GUILayout.Width(48f)))
                {
                    pendingField = field;
                    pendingAppend = true;
                    hasPending = true;
                }
            }

            EditorGUILayout.PropertyField(property, GUIContent.none, true);
        }

        // 填充只能在遍历结束后做：中途 serializedObject.Update 会让迭代器失效
        serializedObject.ApplyModifiedProperties();

        if (!hasPending) return;
        hasPending = false;
        if (targets.Length > 1)
            Debug.LogWarning($"[资源填充] 选中了 {targets.Length} 个对象，只填充第一个「{target.name}」；多目标请用工具窗口");
        ResourceFieldFiller.Fill(target, pendingField, pendingAppend);
        serializedObject.Update();
    }

    private static readonly Dictionary<Type, Dictionary<string, FieldInfo>> nameCache = new();

    private FieldInfo FindField(string path)
    {
        Type type = target.GetType();
        if (!nameCache.TryGetValue(type, out var map))
        {
            map = new Dictionary<string, FieldInfo>();
            foreach (var field in ResourceFieldFiller.ResourceFields(type)) map[field.Name] = field;
            nameCache[type] = map;
        }
        return map.TryGetValue(path, out var found) ? found : null;
    }
}

[CustomEditor(typeof(ResourceManager))]
public class ResourceManagerEditor : AssetListInspectorBase { }

[CustomEditor(typeof(AssetsManager))]
public class AssetsManagerEditor : AssetListInspectorBase { }
