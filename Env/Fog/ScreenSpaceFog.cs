using UnityEngine;

// 挂在相机上：OnRenderImage 里按深度做距离雾与高度雾，一次全屏绘制，不改动场景里任何材质
[RequireComponent(typeof(Camera))]
public class ScreenSpaceFog : MonoBehaviour
{
    public const string ShaderName = "Hidden/Fog/ScreenFog";

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

    [Header("外观")]
    [Tooltip("雾色，场景颜色按雾浓度插值到它")]
    public Color fogColor = new Color(0.251f, 0.322f, 0.349f);

    [Tooltip("全屏雾 shader，添加组件时自动填入")]
    public Shader fogShader;

    private static readonly int FogColorId = Shader.PropertyToID("_FogColor");
    private static readonly int DistanceStartId = Shader.PropertyToID("_DistanceStart");
    private static readonly int DistanceEndId = Shader.PropertyToID("_DistanceEnd");
    private static readonly int DistanceDensityId = Shader.PropertyToID("_DistanceDensity");
    private static readonly int HeightLowId = Shader.PropertyToID("_HeightLow");
    private static readonly int HeightHighId = Shader.PropertyToID("_HeightHigh");
    private static readonly int HeightDensityId = Shader.PropertyToID("_HeightDensity");
    private static readonly int EnableDistanceId = Shader.PropertyToID("_EnableDistance");
    private static readonly int EnableHeightId = Shader.PropertyToID("_EnableHeight");
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
        material.SetColor(FogColorId, fogColor);
        material.SetFloat(DistanceStartId, distanceStart);
        material.SetFloat(DistanceEndId, Mathf.Max(distanceEnd, distanceStart + 1f));
        material.SetFloat(DistanceDensityId, distanceDensity);
        material.SetFloat(HeightLowId, heightLow);
        material.SetFloat(HeightHighId, Mathf.Max(heightHigh, heightLow + 1f));
        material.SetFloat(HeightDensityId, heightDensity);
        material.SetFloat(EnableDistanceId, enableDistanceFog ? 1f : 0f);
        material.SetFloat(EnableHeightId, enableHeightFog ? 1f : 0f);
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
    }
#endif
}
