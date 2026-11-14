using System.Collections.Generic;
using UnityEngine;

public class VfxTest : MonoBehaviour
{
    private struct VfxEntry
    {
        public string label;
        public ResourceType type;
        public int index;
        public bool configured;
    }

    private static readonly string[] CategoryNames = { "Bullet", "Shield", "RangeMagic", "MagicCircle", "Buff" };
    private static readonly ResourceType[] CategoryTypes =
    {
        ResourceType.BulletVFX, ResourceType.ShieldVFX, ResourceType.RangeMagicVFX,
        ResourceType.MagicCircleVFX, ResourceType.BuffVFX,
    };

    private readonly List<VfxEntry> entries = new();
    private readonly List<int> categoryStarts = new();
    private Vector2 scroll;
    private int selected = -1;
    private int selectVersion;   // 连续点选时丢弃上一份迟到的加载结果
    private int currentTab;
    private GameObject currentVfx;
    private float angle;
    private Rect windowRect = new(20f, 20f, 300f, 600f);

    [Tooltip("绕圈半径（米）")]
    [SerializeField] private float radius = 5f;
    [Tooltip("角速度（度/秒）")]
    [SerializeField] private float angularSpeed = 90f;
    [Tooltip("OnGUI 整体缩放（编辑器小窗口看不清时调大）")]
    [SerializeField] private float guiScale = 1.5f;
    [Tooltip("接管相机看向原点（关闭则不动相机）")]
    [SerializeField] private bool camControl = true;
    [Tooltip("相机距原点的水平距离（z = -距离）")]
    [SerializeField] private float camDistance = 15f;
    [Tooltip("相机距原点的垂直高度")]
    [SerializeField] private float camHeight = 8f;

    private Camera testCam;

    private void Start()
    {
        BuildEntries();
        EnsureCamera();
    }

    private void LateUpdate()
    {
        if (!camControl || testCam == null) return;
        Vector3 pos = new Vector3(0f, camHeight, -camDistance); // x=0、z<0，朝向 z+ 看向原点
        testCam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(-pos, Vector3.up));
    }

    // 复用主相机，测试场景没有则补一个
    private void EnsureCamera()
    {
        testCam = Camera.main;
        if (testCam == null)
        {
            var go = new GameObject("VfxTest Camera");
            testCam = go.AddComponent<Camera>();
            go.tag = "MainCamera";
        }
    }

    private void Update()
    {
        if (currentVfx == null) return;
        angle = Mathf.Repeat(angle + angularSpeed * Time.deltaTime, 360f);
        OrbitPose(angle, out Vector3 pos, out Quaternion rot);
        currentVfx.transform.SetPositionAndRotation(pos, rot);
    }

    // 绕世界原点转圈（贴地 y=0）：位置在半径圆周上，朝向沿运动切线
    private void OrbitPose(float deg, out Vector3 pos, out Quaternion rot)
    {
        float rad = deg * Mathf.Deg2Rad;
        pos = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * radius;
        rot = Quaternion.LookRotation(new Vector3(-Mathf.Sin(rad), 0f, Mathf.Cos(rad)), Vector3.up);
    }

    private void OnGUI()
    {
        if (entries.Count == 0 && Tool.ResourceManager != null) BuildEntries();
        Matrix4x4 oldMatrix = GUI.matrix;
        GUIUtility.ScaleAroundPivot(new Vector2(guiScale, guiScale), Vector2.zero); // 整体放大，适配编辑器小窗口
        windowRect.height = Mathf.Min(windowRect.height, Screen.height / guiScale);
        windowRect = GUILayout.Window(GetInstanceID(), windowRect, DrawWindow, "VfxTest 特效试放");
        GUI.matrix = oldMatrix;
    }

    private void BuildEntries()
    {
        entries.Clear();
        categoryStarts.Clear();
        var mgr = Tool.ResourceManager;
        if (mgr == null) return;
        for (int c = 0; c < CategoryTypes.Length; c++)
        {
            categoryStarts.Add(entries.Count);
            ResourceType type = CategoryTypes[c];
            int count = mgr.AssetCount(type);
            for (int i = 0; i < count; i++)
            {
                entries.Add(new VfxEntry
                {
                    label = $"{CategoryNames[c]} {i}",
                    type = type,
                    index = i,
                    configured = mgr.IsAssetConfigured(type, i),
                });
            }
        }
    }

    private void DrawWindow(int id)
    {
        if (Tool.ResourceManager == null)
        {
            GUILayout.Label("场景中未找到 ResourceManager");
            return;
        }

        GUILayout.Label($"半径 {radius:F1} m");
        radius = GUILayout.HorizontalSlider(radius, 0f, 10f);
        GUILayout.Label($"角速度 {angularSpeed:F0} °/s");
        angularSpeed = GUILayout.HorizontalSlider(angularSpeed, 10f, 360f);

        camControl = GUILayout.Toggle(camControl, "接管相机看向原点");
        GUILayout.Label($"相机水平距离 {camDistance:F1} m");
        camDistance = GUILayout.HorizontalSlider(camDistance, 1f, 100f);
        GUILayout.Label($"相机垂直高度 {camHeight:F1} m");
        camHeight = GUILayout.HorizontalSlider(camHeight, 0f, 50f);

        string stopText = selected >= 0 && selected < entries.Count
            ? $"停止（当前：{entries[selected].label}）"
            : "停止";
        if (GUILayout.Button(stopText)) Select(-1);

        if (currentTab >= categoryStarts.Count) currentTab = 0;
        currentTab = GUILayout.Toolbar(currentTab, CategoryNames);
        int start = categoryStarts[currentTab];
        int next = currentTab + 1 < categoryStarts.Count ? categoryStarts[currentTab + 1] : entries.Count;

        scroll = GUILayout.BeginScrollView(scroll);
        for (int i = start; i < next; i++)
        {
            var e = entries[i];
            GUI.enabled = e.configured;
            string text = (i == selected ? "▶ " : "") + e.label + (e.configured ? "" : "（空）");
            if (GUILayout.Button(text)) Select(i);
        }
        GUI.enabled = true;
        GUILayout.EndScrollView();
    }

    // 特效按需加载：切换时旧实例立即销毁，迟到的加载结果直接丢掉
    private void Select(int index)
    {
        if (currentVfx != null) Destroy(currentVfx);
        currentVfx = null;
        selected = index;
        if (index < 0 || index >= entries.Count || !entries[index].configured) return;

        OrbitPose(angle, out Vector3 pos, out Quaternion rot);
        VfxEntry entry = entries[index];
        int version = ++selectVersion;
        Tool.VfxManager.LoadVfx(entry.type, entry.index, pos, rot, obj =>
        {
            if (obj == null) return;
            if (version != selectVersion)
            {
                Destroy(obj);
                return;
            }
            currentVfx = obj;
            obj.name = $"VfxTest_{obj.name}";
        });
    }
}
