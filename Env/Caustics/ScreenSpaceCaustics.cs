using UnityEngine;
using UnityEngine.Rendering;

// 挂在相机上：在画完不透明物体后全屏加色叠加焦散，不需要改动场景里任何材质
[RequireComponent(typeof(Camera))]
public class ScreenSpaceCaustics : MonoBehaviour
{
    public const string ShaderName = "Hidden/Caustics/ScreenProjection";

    [Header("贴图")]
    [Tooltip("焦散网纹：Assets/Files/Shaders/Caustics/T_Caustics")]
    public Texture causticsMap;

    [Tooltip("流动扰动图，rg 通道当偏移：T_CausticFlowMap")]
    public Texture flowMap;

    [Tooltip("全屏投影 shader，添加组件时自动填入")]
    public Shader projectionShader;

    [Header("外观")]
    [Tooltip("焦散颜色，亮度可超过 1 配合 Bloom 出光晕")]
    public Color color = Color.white;

    [Tooltip("亮度总开关，后续接昼夜系统改这一个值即可")]
    [Min(0f)] public float intensity = 1f;

    [Tooltip("世界空间平铺：1 米对应多少循环，越大网纹越密")]
    [Min(0f)] public float tiling = 0.1f;

    [Tooltip("三层网纹各自的滚动速度（度/秒量级，±都可用）")]
    public Vector3 layerSpeeds = new Vector3(0.05f, 0.075f, 0.1f);

    [Range(0f, 1f)]
    [Tooltip("流动扰动强度：0 = 网纹只平移不变形")]
    public float flowIntensity = 0.3f;

    [Tooltip("流动扰动的采样缩放，越大扰动越密")]
    [Min(0f)] public float flowScale = 0.1f;

    [Range(0f, 1f)]
    [Tooltip("法线遮罩偏移：越大立面上也越亮，0.35 时立面约为正面的 12%")]
    public float normalBias = 0.35f;

    private static readonly int CausticsMapId = Shader.PropertyToID("_CausticsMap");
    private static readonly int FlowMapId = Shader.PropertyToID("_FlowMap");
    private static readonly int ColorId = Shader.PropertyToID("_CausticsColor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int TilingId = Shader.PropertyToID("_Tiling");
    private static readonly int SpeedId = Shader.PropertyToID("_Speed");
    private static readonly int FlowIntensityId = Shader.PropertyToID("_FlowIntensity");
    private static readonly int FlowScaleId = Shader.PropertyToID("_FlowScale");
    private static readonly int NormalBiasId = Shader.PropertyToID("_NormalBias");
    private static readonly int CamPosId = Shader.PropertyToID("_CamPos");
    private static readonly int CornerBLId = Shader.PropertyToID("_CornerBL");
    private static readonly int CornerBRId = Shader.PropertyToID("_CornerBR");
    private static readonly int CornerTLId = Shader.PropertyToID("_CornerTL");
    private static readonly int CornerTRId = Shader.PropertyToID("_CornerTR");

    private Camera cam;
    private Material material;
    private CommandBuffer buffer;
    private Mesh quad;
    private bool ready;
    private bool renderActive;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (projectionShader == null)
        {
            Debug.LogError("[ScreenSpaceCaustics] 未指定 projectionShader，组件不生效", this);
            return;
        }

        if (causticsMap == null || flowMap == null)
        {
            Debug.LogError("[ScreenSpaceCaustics] causticsMap / flowMap 未指定，组件不生效", this);
            return;
        }

        if (cam.actualRenderingPath != RenderingPath.Forward)
        {
            Debug.LogWarning($"[ScreenSpaceCaustics] 相机「{cam.name}」不是 Forward 路径，AfterForwardOpaque 不会触发，组件不生效", this);
            return;
        }

        if (cam.orthographic)
        {
            Debug.LogWarning($"[ScreenSpaceCaustics] 相机「{cam.name}」是正交投影，视锥角方向退化为平行线，组件不生效", this);
            return;
        }

        material = new Material(projectionShader);
        material.hideFlags = HideFlags.HideAndDontSave;
        ApplyMaterial();

        quad = BuildQuad();
        buffer = new CommandBuffer { name = "ScreenSpaceCaustics" };
        buffer.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
        cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, buffer);
        renderActive = true;
        ready = true;
    }

    // 焦散总开关：摘掉命令缓冲，关闭时连一次全屏绘制都不提交（不是把 intensity 归零）
    public void SetRenderEnabled(bool value)
    {
        if (!ready || value == renderActive) return;
        renderActive = value;
        if (value) cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, buffer);
        else cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, buffer);
    }

    // Unity 在渲染前读该标志，别处覆盖时这里每帧补回来
    private void OnPreCull()
    {
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void Update()
    {
        if (!ready) return;
        ApplyMaterial();
    }

    private void ApplyMaterial()
    {
        material.SetTexture(CausticsMapId, causticsMap);
        material.SetTexture(FlowMapId, flowMap);
        material.SetColor(ColorId, color);
        material.SetFloat(IntensityId, intensity);
        material.SetFloat(TilingId, tiling);
        material.SetVector(SpeedId, layerSpeeds);
        material.SetFloat(FlowIntensityId, flowIntensity);
        material.SetFloat(FlowScaleId, flowScale);
        material.SetFloat(NormalBiasId, normalBias);

        UpdateFrustumCorners();
    }

    // 四个视锥角方向（裁剪空间角点反投影到世界空间，归一到视空间 z = -1），与像素同一套坐标
    private void UpdateFrustumCorners()
    {
        Matrix4x4 invProj = cam.projectionMatrix.inverse;
        Matrix4x4 camToWorld = cam.cameraToWorldMatrix;
        Vector3 origin = camToWorld.GetColumn(3);

        material.SetVector(CamPosId, origin);
        material.SetVector(CornerBLId, Corner(invProj, camToWorld, origin, -1f, -1f));
        material.SetVector(CornerBRId, Corner(invProj, camToWorld, origin, 1f, -1f));
        material.SetVector(CornerTLId, Corner(invProj, camToWorld, origin, -1f, 1f));
        material.SetVector(CornerTRId, Corner(invProj, camToWorld, origin, 1f, 1f));
    }

    private static Vector3 Corner(Matrix4x4 invProj, Matrix4x4 camToWorld, Vector3 origin, float ndcX, float ndcY)
    {
        Vector4 p = invProj * new Vector4(ndcX, ndcY, 1f, 1f);
        Vector3 view = new Vector3(p.x, p.y, p.z) / p.w;
        view /= -view.z; // 归一到视空间 z = -1，这样乘 eyeDepth 就落在这个像素看到的位置
        return camToWorld.MultiplyPoint3x4(view) - origin;
    }

    private void OnDestroy()
    {
        if (buffer == null) return;

        if (cam != null) cam.RemoveCommandBuffer(CameraEvent.AfterForwardOpaque, buffer);
        buffer.Release();
        buffer = null;

        Destroy(material);
        Destroy(quad);
    }

    private static Mesh BuildQuad()
    {
        // 顶点直接给裁剪空间坐标，shader 里不再走矩阵，包围盒放大避免被剔除
        var mesh = new Mesh { name = "ScreenSpaceCausticsQuad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[]
        {
            new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
            new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f),
        };
        mesh.uv = new[]
        {
            new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0f, 1f), new Vector2(1f, 1f),
        };
        mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e6f);
        return mesh;
    }

#if UNITY_EDITOR
    private void Reset()
    {
        projectionShader = Shader.Find(ShaderName);
        if (projectionShader == null)
            Debug.LogError($"[ScreenSpaceCaustics] 找不到 shader「{ShaderName}」", this);
    }
#endif
}
