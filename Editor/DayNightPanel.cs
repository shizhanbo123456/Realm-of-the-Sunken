using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 昼夜调试面板：时间轴预览 + 参数帧列表 + 组件收集报错
public class DayNightPanel : EditorWindow
{
    private EnvironmentManager target;
    private SerializedObject serialized;
    private SerializedProperty profilesProperty;
    private Vector2 scroll;
    private float previewTime = 1f;
    private string error = "";

    [MenuItem("Tools/昼夜面板")]
    public static void Open()
    {
        GetWindow<DayNightPanel>("昼夜");
    }

    private void OnEnable()
    {
        FindTarget();
    }

    private void OnGUI()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
        if (GUILayout.Button("重新找场景中的 EnvironmentManager", EditorStyles.toolbarButton)) FindTarget();
        if (GUILayout.Button("重新收集组件", EditorStyles.toolbarButton)) Collect();
        EditorGUILayout.EndHorizontal();

        if (target == null)
        {
            EditorGUILayout.HelpBox("当前场景没有 EnvironmentManager。", MessageType.Error);
            return;
        }

        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("时间（0/2 午夜、1 正午）", EditorStyles.boldLabel);

        EditorGUI.BeginChangeCheck();
        previewTime = EditorGUILayout.Slider(previewTime, 0f, DayNightTimeline.CycleLength);
        if (EditorGUI.EndChangeCheck()) Preview();

        EditorGUILayout.LabelField($"当前时间：{previewTime:F3}");

        if (Application.isPlaying && Tool.BattleManager != null)
        {
            EditorGUILayout.LabelField($"运行时 CycleTime：{BattleManager.CycleTime:F3}，{(BattleManager.IsDay ? "白天" : "夜晚")}");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("场景组件", EditorStyles.boldLabel);
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.ObjectField("太阳（平行光）", target.sun, typeof(Light), true);
            EditorGUILayout.ObjectField("ScreenSpaceFog", target.fog, typeof(ScreenSpaceFog), true);
            EditorGUILayout.ObjectField("ScreenSpaceCaustics", target.caustics, typeof(ScreenSpaceCaustics), true);
            EditorGUILayout.ObjectField("VolumetricLight", target.volumetricLight, typeof(VolumetricLights.VolumetricLight), true);
            EditorGUILayout.ObjectField("VolumetricAreaLightFollow", target.volumetricFollow, typeof(VolumetricAreaLightFollow), true);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("参数帧（按时间戳循环插值）", EditorStyles.boldLabel);
        serialized.Update();
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(profilesProperty, new GUIContent("Profiles"), true);
        if (EditorGUI.EndChangeCheck())
        {
            serialized.ApplyModifiedProperties();
            Preview();
        }

        if (error.Length > 0)
        {
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        EditorGUILayout.EndScrollView();
    }

    private void FindTarget()
    {
        var found = Object.FindObjectsOfType<EnvironmentManager>();
        if (found.Length == 0)
        {
            target = null;
            serialized = null;
            profilesProperty = null;
            error = "当前场景没有 EnvironmentManager。";
            return;
        }
        if (found.Length > 1)
        {
            target = null;
            serialized = null;
            profilesProperty = null;
            error = $"当前场景有 {found.Length} 个 EnvironmentManager，无法确定操作对象。";
            return;
        }

        target = found[0];
        serialized = new SerializedObject(target);
        profilesProperty = serialized.FindProperty("profiles");
        previewTime = target.time;
        error = "";
        Collect();
    }

    private void Collect()
    {
        if (target == null) return;
        try
        {
            target.CollectComponents();
            target.ValidateComponents();
            error = "";
        }
        catch (System.Exception e)
        {
            error = e.Message;
        }
    }

    private void Preview()
    {
        if (target == null) return;
        try
        {
            target.CollectComponents();
            target.ValidateComponents();

            // 运行时时间由 BattleManager 持有，改时钟才算真的改
            if (Application.isPlaying && Tool.BattleManager != null) Tool.BattleManager.SetCycleTime(previewTime);
            else target.ApplyTime(previewTime);

            error = "";
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
        }
        catch (System.Exception e)
        {
            error = e.Message;
        }
    }
}
