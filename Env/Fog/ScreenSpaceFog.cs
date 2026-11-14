using UnityEngine;

// 挂在相机上：OnRenderImage 里按深度做距离雾与高度雾，一次全屏绘制，不改动场景里任何材质
[RequireComponent(typeof(Camera))]
public class ScreenSpaceFog : MonoBehaviour
{
    public const string ShaderName = "Hidden/Fog/ScreenFog";
    public const string WaterCausticsPath = "Assets/Files/Shaders/Caustics/T_Caustics.TGA";

    [Header("距离雾")]
    [Tooltip("关闭后只剩高度雾")]
    public bool enableDistanceFog = true;

    [Tooltip("此距离以内完全没有距离雾（米）")]
    [Min(0f)] public float distanceStart = 15f;

    [Tooltip("达到此距离时距离雾到最大浓度（米）")]
    [Min(0f)] public float distanceEnd = 120f;

    [Range(0f, 1f)]
    [Tooltip("距离最远处的最大混合强度")]
    public float distanceDensity = 0.85f;

    [HideInInspector] public float distanceScale = 1f;

    [Header("高度雾")]
    [Tooltip("关闭后只剩距离雾")]
    public bool enableHeightFog = true;

    [Tooltip("此世界高度以下雾气满浓度（米）")]
    public float heightLow = 0f;

    [Tooltip("此世界高度以上没有雾气（米）")]
    public float heightHigh = 25f;

    [Range(0f, 1f)]
    [Tooltip("低处的最大混合强度")]
    public float heightDensity = 0.8f;

    [Header("雾色渐变")]
    [Tooltip("朝天空 / 屏幕上方取的雾色，整体偏浅")]
    public Color fogColorTop = new Color(0.36f, 0.45f, 0.49f);

    [Tooltip("朝地面 / 屏幕下方取的雾色，整体偏深")]
    public Color fogColorBottom = new Color(0.17f, 0.22f, 0.25f);

    [Tooltip("屏幕边角处混向的雾色，通常更暗")]
    public Color fogColorEdge = new Color(0.10f, 0.14f, 0.17f);

    [Range(-1f, 1f)]
    [Tooltip("上下渐变中心，世界仰角与屏幕上下共用")]
    public float angleCenter = 0f;

    [Range(0.05f, 1f)]
    [Tooltip("渐变过渡柔和度，越小分界越硬")]
    public float angleSoftness = 0.6f;

    [Range(0f, 1f)]
    [Tooltip("0 只按世界仰角取色，1 只按屏幕上下取色")]
    public float screenAngleWeight = 0.5f;

    [Range(0f, 0.95f)]
    [Tooltip("屏幕半径超过此值才开始混边角色")]
    public float edgeStart = 0.55f;

    [Range(0f, 1f)]
    [Tooltip("边角暗色的混合强度")]
    public float edgeIntensity = 0.5f;

    [Header("水面（仅天空区域）")]
    [Tooltip("关闭后天空只剩雾色渐变")]
    public bool enableWaterSurface = true;

    [Tooltip("水面光斑贴图，复用 Assets/Files/Shaders/Caustics/T_Caustics")]
    public Texture waterCausticsMap;

    [Range(0f, 90f)]
    [Tooltip("高于此仰角的天空区域视为水面（度），48.6 是水下看水面的全反射临界角")]
    public float waterAngleDeg = 48.6f;

    [Range(0.1f, 20f)]
    [Tooltip("水面与雾的过渡宽度（度）")]
    public float waterSoftnessDeg = 5f;

    [Tooltip("水面基色")]
    public Color waterColor = new Color(0.60f, 0.76f, 0.80f);

    [Min(0f)]
    [Tooltip("光斑密度，越大光斑越密")]
    public float waterTiling = 1.5f;

    [Range(0f, 1f)]
    [Tooltip("光斑明暗强度")]
    public float waterPattern = 0.55f;

    [Range(0.1f, 20f)]
    [Tooltip("贴边亮带宽度（度），从阈值边界往内衰减")]
    public float waterRimWidthDeg = 8f;

    [Tooltip("贴边亮带颜色")]
    public Color waterRimColor = new Color(0.80f, 0.92f, 0.95f);

    [Range(0f, 1f)]
    [Tooltip("贴边亮带强度")]
    public float waterRimIntensity = 0.5f;

    [Tooltip("全屏雾 shader，添加组件时自动填入")]
    public Shader fogShader;

    private static readonly int FogColorTopId = Shader.PropertyToID("_FogColorTop");
    private static readonly int FogColorBottomId = Shader.PropertyToID("_FogColorBottom");
    private static readonly int FogColorEdgeId = Shader.PropertyToID("_FogColorEdge");
    private static readonly int AngleCenterId = Shader.PropertyToID("_AngleCenter");
    private static readonly int AngleSoftnessId = Shader.PropertyToID("_AngleSoftness");
    private static readonly int ScreenAngleWeightId = Shader.PropertyToID("_ScreenAngleWeight");
    private static readonly int EdgeStartId = Shader.PropertyToID("_EdgeStart");
    private static readonly int EdgeIntensityId = Shader.PropertyToID("_EdgeIntensity");
    private static readonly int DistanceStartId = Shader.PropertyToID("_DistanceStart");
    private static readonly int DistanceEndId = Shader.PropertyToID("_DistanceEnd");
    private static readonly int DistanceDensityId = Shader.PropertyToID("_DistanceDensity");
    private static readonly int HeightLowId = Shader.PropertyToID("_HeightLow");
    private static readonly int HeightHighId = Shader.PropertyToID("_HeightHigh");
    private static readonly int HeightDensityId = Shader.PropertyToID("_HeightDensity");
    private static readonly int EnableDistanceId = Shader.PropertyToID("_EnableDistance");
    private static readonly int EnableHeightId = Shader.PropertyToID("_EnableHeight");
    private static readonly int EnableWaterId = Shader.PropertyToID("_EnableWater");
    private static readonly int WaterCausticsMapId = Shader.PropertyToID("_WaterCausticsMap");
    private static readonly int WaterAngleId = Shader.PropertyToID("_WaterAngle");
    private static readonly int WaterSoftnessId = Shader.PropertyToID("_WaterSoftness");
    private static readonly int WaterColorId = Shader.PropertyToID("_WaterColor");
    private static readonly int WaterTilingId = Shader.PropertyToID("_WaterTiling");
    private static readonly int WaterPatternId = Shader.PropertyToID("_WaterPattern");
    private static readonly int WaterRimWidthId = Shader.PropertyToID("_WaterRimWidth");
    private static readonly int WaterRimColorId = Shader.PropertyToID("_WaterRimColor");
    private static readonly int WaterRimIntensityId = Shader.PropertyToID("_WaterRimIntensity");
    private static readonly int CamPosId = Shader.PropertyToID("_CamPos");
    private static readonly int CornerBLId = Shader.PropertyToID("_CornerBL");
    private static readonly int CornerBRId = Shader.PropertyToID("_CornerBR");
    private static readonly int CornerTLId = Shader.PropertyToID("_CornerTL");
    private static readonly int CornerTRId = Shader.PropertyToID("_CornerTR");

    private Camera cam;
    private Material material;
    private bool ready;

    private void Awake()
    {
        cam = GetComponent<Camera>();

        if (fogShader == null)
        {
            Debug.LogError("[ScreenSpaceFog] 未指定 fogShader，组件不生效", this);
            return;
        }

        if (cam.orthographic)
        {
            Debug.LogWarning($"[ScreenSpaceFog] 相机「{cam.name}」是正交投影，视锥角方向退化为平行线，组件不生效", this);
            return;
        }

        if (enableWaterSurface && waterCausticsMap == null)
        {
            Debug.LogError("[ScreenSpaceFog] 未指定 waterCausticsMap（Assets/Files/Shaders/Caustics/T_Caustics），水面关闭", this);
            enableWaterSurface = false;
        }

        material = new Material(fogShader) { hideFlags = HideFlags.HideAndDontSave };
        cam.depthTextureMode |= DepthTextureMode.Depth;
        ready = true;
    }

    // 别处覆盖深度模式时每帧补回来
    private void OnPreCull()
    {
        cam.depthTextureMode |= DepthTextureMode.Depth;
    }

    private void OnRenderImage(RenderTexture src, RenderTexture dst)
    {
        if (!ready)
        {
            Graphics.Blit(src, dst);
            return;
        }

        ApplyMaterial();
        Graphics.Blit(src, dst, material);
    }

    private void ApplyMaterial()
    {
        material.SetColor(FogColorTopId, fogColorTop);
        material.SetColor(FogColorBottomId, fogColorBottom);
        material.SetColor(FogColorEdgeId, fogColorEdge);
        material.SetFloat(AngleCenterId, angleCenter);
        material.SetFloat(AngleSoftnessId, angleSoftness);
        material.SetFloat(ScreenAngleWeightId, screenAngleWeight);
        material.SetFloat(EdgeStartId, edgeStart);
        material.SetFloat(EdgeIntensityId, edgeIntensity);
        material.SetFloat(DistanceStartId, distanceStart * distanceScale);
        material.SetFloat(DistanceEndId, Mathf.Max(distanceEnd, distanceStart + 1f) * distanceScale);
        material.SetFloat(DistanceDensityId, distanceDensity);
        material.SetFloat(HeightLowId, heightLow);
        material.SetFloat(HeightHighId, Mathf.Max(heightHigh, heightLow + 1f));
        material.SetFloat(HeightDensityId, heightDensity);
        material.SetFloat(EnableDistanceId, enableDistanceFog ? 1f : 0f);
        material.SetFloat(EnableHeightId, enableHeightFog ? 1f : 0f);
        material.SetFloat(EnableWaterId, enableWaterSurface ? 1f : 0f);
        if (waterCausticsMap != null) material.SetTexture(WaterCausticsMapId, waterCausticsMap);
        material.SetFloat(WaterAngleId, waterAngleDeg);
        material.SetFloat(WaterSoftnessId, Mathf.Max(waterSoftnessDeg, 0.1f));
        material.SetColor(WaterColorId, waterColor);
        material.SetFloat(WaterTilingId, waterTiling);
        material.SetFloat(WaterPatternId, waterPattern);
        material.SetFloat(WaterRimWidthId, Mathf.Max(waterRimWidthDeg, 0.1f));
        material.SetColor(WaterRimColorId, waterRimColor);
        material.SetFloat(WaterRimIntensityId, waterRimIntensity);
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
        Destroy(material);
    }

#if UNITY_EDITOR
    private void Reset()
    {
        fogShader = Shader.Find(ShaderName);
        if (fogShader == null)
            Debug.LogError($"[ScreenSpaceFog] 找不到 shader「{ShaderName}」", this);

        waterCausticsMap = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture>(WaterCausticsPath);
    }
#endif
}
