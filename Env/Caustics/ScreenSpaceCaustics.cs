using UnityEngine;
using UnityEngine.Rendering;

using Ros.Config;
// 挂在相机上：在画完不透明物体后全屏加色叠加焦散，不需要改动场景里任何材质
[RequireComponent(typeof(Camera))]
public class ScreenSpaceCaustics : MonoBehaviour
{
    public const string ShaderName = "Hidden/Caustics/ScreenProjection";
    public const string OcclusionResourcePath = "Effects/Caustics/CausticsOcclusion";

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

    [Header("遮挡（地形工具烘焙的高度图）")]
    [Tooltip("关闭后不读高度图，屋顶下的地面也会有焦散")]
    public bool useOcclusion = true;

    [Tooltip("该列最高面比当前像素高出多少米判定为被遮挡")]
    [Min(0f)] public float occlusionBias = 0.5f;

    [Tooltip("遮挡过渡带宽度（米），太小会出硬边")]
    [Min(0.01f)] public float occlusionFade = 1f;

    private static readonly int CausticsMapId = Shader.PropertyToID("_CausticsMap");
    private static readonly int FlowMapId = Shader.PropertyToID("_FlowMap");
    private static readonly int OcclusionMapId = Shader.PropertyToID("_OcclusionMap");
    private static readonly int ColorId = Shader.PropertyToID("_CausticsColor");
    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int TilingId = Shader.PropertyToID("_Tiling");
    private static readonly int SpeedId = Shader.PropertyToID("_Speed");
    private static readonly int FlowIntensityId = Shader.PropertyToID("_FlowIntensity");
    private static readonly int FlowScaleId = Shader.PropertyToID("_FlowScale");
    private static readonly int NormalBiasId = Shader.PropertyToID("_NormalBias");
    private static readonly int MapMinId = Shader.PropertyToID("_MapMin");
    private static readonly int MapSizeId = Shader.PropertyToID("_MapSize");
    private static readonly int OcclusionBiasId = Shader.PropertyToID("_OcclusionBias");
    private static readonly int OcclusionFadeId = Shader.PropertyToID("_OcclusionFade");
    private static readonly int CamPosId = Shader.PropertyToID("_CamPos");
    private static readonly int CornerBLId = Shader.PropertyToID("_CornerBL");
    private static readonly int CornerBRId = Shader.PropertyToID("_CornerBR");
    private static readonly int CornerTLId = Shader.PropertyToID("_CornerTL");
    private static readonly int CornerTRId = Shader.PropertyToID("_CornerTR");

    private Camera cam;
    private Material material;
    private CommandBuffer buffer;
    private Mesh quad;
    private Texture occlusionMap;
    private bool occlusionActive;
    private bool missingOcclusionWarned;
    private bool ready;
    private float mapMin;
    private float mapSize;

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

        occlusionActive = useOcclusion;
        if (occlusionActive)
        {
            occlusionMap = Resources.Load<Texture2D>(OcclusionResourcePath);
            if (occlusionMap == null)
            {
                Debug.LogWarning($"[ScreenSpaceCaustics] 找不到 Resources/{OcclusionResourcePath}，本次不启用遮挡（用地形工具「烘焙焦散遮挡图」生成）", this);
                occlusionActive = false;
            }
        }

        material = new Material(projectionShader);
        material.hideFlags = HideFlags.HideAndDontSave;
        ApplyMaterial();

        quad = BuildQuad();
        buffer = new CommandBuffer { name = "ScreenSpaceCaustics" };
        buffer.DrawMesh(quad, Matrix4x4.identity, material, 0, 0);
        cam.AddCommandBuffer(CameraEvent.AfterForwardOpaque, buffer);
        ready = true;
    }

    private void Start()
    {
        // 地图范围来自配表，晚于 Awake 读取以确保 ConfigManager 已加载
        mapMin = GlobalVariable.RangeMin(global_variablesEnum.terrain_size);
        mapSize = GlobalVariable.RangeLength(global_variablesEnum.terrain_size);
        if (mapSize <= 0f)
        {
            Debug.LogWarning("[ScreenSpaceCaustics] 配表 terrain_size 无效（<=0），遮挡判定关闭", this);
            occlusionActive = false;
        }
    }

    // Unity 在渲染前读该标志，别处覆盖时这里每帧补回来
    private void OnPreCull()
    {
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void Update()
    {
        if (!ready) return;
        SyncOcclusionToggle();
        ApplyMaterial();
    }

    // 允许运行时勾掉/勾上遮挡做 A/B 对比；勾上但资产缺失时只警告一次
    private void SyncOcclusionToggle()
    {
        if (useOcclusion == occlusionActive) return;

        if (!useOcclusion)
        {
            occlusionActive = false;
            return;
        }

        if (occlusionMap == null) occlusionMap = Resources.Load<Texture2D>(OcclusionResourcePath);
        if (occlusionMap == null)
        {
            if (!missingOcclusionWarned)
            {
                Debug.LogWarning($"[ScreenSpaceCaustics] 找不到 Resources/{OcclusionResourcePath}，遮挡仍关闭", this);
                missingOcclusionWarned = true;
            }
            return;
        }

        occlusionActive = true;
    }

    private void ApplyMaterial()
    {
        material.SetTexture(CausticsMapId, causticsMap);
        material.SetTexture(FlowMapId, flowMap);
        material.SetTexture(OcclusionMapId, occlusionActive ? occlusionMap : Texture2D.blackTexture);
        material.SetColor(ColorId, color);
        material.SetFloat(IntensityId, intensity);
        material.SetFloat(TilingId, tiling);
        material.SetVector(SpeedId, layerSpeeds);
        material.SetFloat(FlowIntensityId, flowIntensity);
        material.SetFloat(FlowScaleId, flowScale);
        material.SetFloat(NormalBiasId, normalBias);
        material.SetFloat(OcclusionBiasId, occlusionBias);
        material.SetFloat(OcclusionFadeId, occlusionFade);

        // MapSize 为 0 时 shader 里整段遮挡判定被跳过
        material.SetVector(MapMinId, new Vector4(mapMin, 0f, mapMin, 0f));
        material.SetFloat(MapSizeId, occlusionActive ? mapSize : 0f);

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
