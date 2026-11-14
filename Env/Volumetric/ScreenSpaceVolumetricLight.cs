using UnityEngine;

// 挂在相机上：先沿光线方向烘一张遮蔽距离图，再沿视线步进累加散射，光柱会被天花板与墙体正确切断
// 与 ScreenSpaceFog 同挂相机时本组件排在其后、后处理层之前，光柱才会被 Bloom 晕开
[RequireComponent(typeof(Camera))]
public class ScreenSpaceVolumetricLight : MonoBehaviour
{
	public const string RaymarchShaderName = "Hidden/Volumetric/ScreenRaymarch";
	public const string CompositeShaderName = "Hidden/Volumetric/ScreenComposite";
	public const string BakeShaderName = "Hidden/Volumetric/BakeOcclusion";
	public const string RaymarchShaderPath = "Assets/Files/Shaders/Volumetric/ScreenRaymarch.shader";
	public const string CompositeShaderPath = "Assets/Files/Shaders/Volumetric/ScreenComposite.shader";
	public const string BakeShaderPath = "Assets/Files/Shaders/Volumetric/BakeOcclusion.shader";

	[Header("光源")]
	[Tooltip("方向光，取它的 forward 作为光线方向；留空则用下面的 lightDirection")]
	public Light lightSource;

	[Tooltip("未指定 lightSource 时的光线传播方向，默认垂直向下")]
	public Vector3 lightDirection = new Vector3(0f, -1f, 0f);

	[Tooltip("散射光颜色，亮度可超过 1 配合 Bloom 出光晕")]
	public Color lightColor = new Color(1f, 0.92f, 0.78f);

	[Tooltip("亮度总开关，后续接昼夜系统改这一个值即可")]
	[Min(0f)] public float intensity = 3f;

	[Header("介质")]
	[Tooltip("此世界高度以下介质满浓度（米）")]
	public float fogBottomY = 0f;

	[Tooltip("此世界高度以上没有介质（米）")]
	public float fogTopY = 25f;

	[Tooltip("介质浓度，越大光柱越浓，0.02~0.1 之间通常够用")]
	[Min(0f)] public float density = 0.06f;

	[Range(0f, 0.95f)]
	[Tooltip("前向散射强度，越大逆光看光柱越亮")]
	public float anisotropy = 0.55f;

	[Range(0f, 1f)]
	[Tooltip("天空区域（无几何）的散射倍数，压低可防止整片天空发白")]
	public float skyScale = 0.35f;

	[Header("步进")]
	[Tooltip("最大步进距离（米），建议小于烘焙范围半径")]
	[Min(1f)] public float maxDistance = 80f;

	[Range(8, 64)]
	[Tooltip("每像素步数，越高越平滑越贵")]
	public int stepCount = 28;

	[Range(1, 4)]
	[Tooltip("缩小倍数，2 为半分辨率；1 最清晰但开销翻倍")]
	public int downsample = 2;

	[Range(0f, 4f)]
	[Tooltip("合成时的抽样半径（半分辨率像素），用来压掉步进噪点")]
	public float blurRadius = 1f;

	[Header("遮蔽图（静态烘焙）")]
	[Tooltip("烘焙范围中心：x/z 为平面位置，y 为高度中点")]
	public Vector3 bakeCenter = Vector3.zero;

	[Tooltip("烘焙范围尺寸：x/z 为平面尺寸，y 为高度；应覆盖所有会遮挡光线的几何")]
	public Vector3 bakeSize = new Vector3(200f, 60f, 200f);

	[Tooltip("只有这些层参与烘焙，角色与怪物建议排除，否则会把它们的模型固化进遮蔽图")]
	public LayerMask bakeLayers = ~0;

	[Tooltip("按 RenderType 标签挑选参与烘焙的物体，透明物体应靠标签排除；留空表示全部替换")]
	public string bakeTag = "Opaque";

	[Range(256, 4096)]
	[Tooltip("遮蔽图分辨率，范围大时要给高一些")]
	public int bakeResolution = 2048;

	[Tooltip("遮蔽过渡带宽度（米），太小光柱边缘会硬")]
	[Min(0.01f)] public float occlusionSoftness = 1.5f;

	[Tooltip("抬起判定偏移（米），用来消除表面自阴影造成的条纹")]
	[Min(0f)] public float occlusionBias = 0.15f;

	[Tooltip("运行时若光源转向就自动重烘，开销较大，只在需要动态太阳时打开")]
	public bool autoRebake = false;

	[Header("Shader")]
	[Tooltip("步进 shader，添加组件时自动填入")]
	public Shader raymarchShader;

	[Tooltip("合成 shader，添加组件时自动填入")]
	public Shader compositeShader;

	[Tooltip("烘焙 shader，添加组件时自动填入")]
	public Shader bakeShader;

	private static readonly int LightColorId = Shader.PropertyToID("_LightColor");
	private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
	private static readonly int DensityId = Shader.PropertyToID("_Density");
	private static readonly int FogBottomYId = Shader.PropertyToID("_FogBottomY");
	private static readonly int FogTopYId = Shader.PropertyToID("_FogTopY");
	private static readonly int AnisotropyId = Shader.PropertyToID("_Anisotropy");
	private static readonly int MaxDistanceId = Shader.PropertyToID("_MaxDistance");
	private static readonly int StepCountId = Shader.PropertyToID("_StepCount");
	private static readonly int SkyScaleId = Shader.PropertyToID("_SkyScale");
	private static readonly int OcclusionSoftnessId = Shader.PropertyToID("_OcclusionSoftness");
	private static readonly int OcclusionBiasId = Shader.PropertyToID("_OcclusionBias");
	private static readonly int LightDirectionId = Shader.PropertyToID("_LightDirection");
	private static readonly int LightVPId = Shader.PropertyToID("_LightVP");
	private static readonly int LightVId = Shader.PropertyToID("_LightV");
	private static readonly int LightOcclusionId = Shader.PropertyToID("_LightOcclusion");
	private static readonly int BlurRadiusId = Shader.PropertyToID("_BlurRadius");
	private static readonly int VolumeTexId = Shader.PropertyToID("_VolumeTex");
	private static readonly int CamPosId = Shader.PropertyToID("_CamPos");
	private static readonly int CornerBLId = Shader.PropertyToID("_CornerBL");
	private static readonly int CornerBRId = Shader.PropertyToID("_CornerBR");
	private static readonly int CornerTLId = Shader.PropertyToID("_CornerTL");
	private static readonly int CornerTRId = Shader.PropertyToID("_CornerTR");

	private Camera cam;
	private Material raymarchMaterial;
	private Material compositeMaterial;
	private RenderTexture bakeRT;
	private Camera bakeCam;
	private Matrix4x4 lightVP;
	private Matrix4x4 lightV;
	private Vector3 bakedDirection;
	private bool ready;

	private void Awake()
	{
		cam = GetComponent<Camera>();

		if (raymarchShader == null || compositeShader == null || bakeShader == null)
		{
			Debug.LogError("[ScreenSpaceVolumetricLight] raymarchShader / compositeShader / bakeShader 未全部指定，组件不生效", this);
			return;
		}

		if (cam.orthographic)
		{
			Debug.LogWarning($"[ScreenSpaceVolumetricLight] 相机「{cam.name}」是正交投影，视锥角方向退化为平行线，组件不生效", this);
			return;
		}

		raymarchMaterial = new Material(raymarchShader) { hideFlags = HideFlags.HideAndDontSave };
		compositeMaterial = new Material(compositeShader) { hideFlags = HideFlags.HideAndDontSave };
		cam.depthTextureMode |= DepthTextureMode.Depth;
	}

	// 放到 Start 烘焙：此时场景物体已经摆好位置，灯光的朝向也已确定
	private void Start()
	{
		Rebake();
	}

	// 别处覆盖深度模式时每帧补回来
	private void OnPreCull()
	{
		cam.depthTextureMode |= DepthTextureMode.Depth;
	}

	private void Update()
	{
		if (!ready || !autoRebake) return;

		if (Vector3.Angle(ResolveDirection(), bakedDirection) > 0.5f) Rebake();
	}

	private void OnRenderImage(RenderTexture src, RenderTexture dst)
	{
		if (!ready)
		{
			Graphics.Blit(src, dst);
			return;
		}

		ApplyMaterial();

		int w = Mathf.Max(1, src.width / downsample);
		int h = Mathf.Max(1, src.height / downsample);
		RenderTexture volume = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
		volume.filterMode = FilterMode.Bilinear;

		Graphics.Blit(Texture2D.blackTexture, volume, raymarchMaterial);
		compositeMaterial.SetTexture(VolumeTexId, volume);
		Graphics.Blit(src, dst, compositeMaterial);

		RenderTexture.ReleaseTemporary(volume);
	}

	private void ApplyMaterial()
	{
		raymarchMaterial.SetVector(LightDirectionId, ResolveDirection());
		raymarchMaterial.SetColor(LightColorId, lightColor);
		raymarchMaterial.SetFloat(IntensityId, intensity);
		raymarchMaterial.SetFloat(DensityId, density);
		raymarchMaterial.SetFloat(FogBottomYId, fogBottomY);
		raymarchMaterial.SetFloat(FogTopYId, Mathf.Max(fogTopY, fogBottomY + 1f));
		raymarchMaterial.SetFloat(AnisotropyId, anisotropy);
		raymarchMaterial.SetFloat(MaxDistanceId, maxDistance);
		raymarchMaterial.SetFloat(StepCountId, stepCount);
		raymarchMaterial.SetFloat(SkyScaleId, skyScale);
		raymarchMaterial.SetFloat(OcclusionSoftnessId, occlusionSoftness);
		raymarchMaterial.SetFloat(OcclusionBiasId, occlusionBias);
		raymarchMaterial.SetTexture(LightOcclusionId, bakeRT);
		raymarchMaterial.SetMatrix(LightVPId, lightVP);
		raymarchMaterial.SetMatrix(LightVId, lightV);

		compositeMaterial.SetFloat(BlurRadiusId, blurRadius);

		UpdateFrustumCorners();
	}

	// 四个视锥角方向（裁剪空间角点反投影到世界空间，归一到视空间 z = -1），与像素同一套坐标
	private void UpdateFrustumCorners()
	{
		Matrix4x4 invProj = cam.projectionMatrix.inverse;
		Matrix4x4 camToWorld = cam.cameraToWorldMatrix;
		Vector3 origin = camToWorld.GetColumn(3);

		raymarchMaterial.SetVector(CamPosId, origin);
		raymarchMaterial.SetVector(CornerBLId, Corner(invProj, camToWorld, origin, -1f, -1f));
		raymarchMaterial.SetVector(CornerBRId, Corner(invProj, camToWorld, origin, 1f, -1f));
		raymarchMaterial.SetVector(CornerTLId, Corner(invProj, camToWorld, origin, -1f, 1f));
		raymarchMaterial.SetVector(CornerTRId, Corner(invProj, camToWorld, origin, 1f, 1f));
	}

	private static Vector3 Corner(Matrix4x4 invProj, Matrix4x4 camToWorld, Vector3 origin, float ndcX, float ndcY)
	{
		Vector4 p = invProj * new Vector4(ndcX, ndcY, 1f, 1f);
		Vector3 view = new Vector3(p.x, p.y, p.z) / p.w;
		view /= -view.z; // 归一到视空间 z = -1，这样乘 eyeDepth 就落在这个像素看到的位置
		return camToWorld.MultiplyPoint3x4(view) - origin;
	}

	private Vector3 ResolveDirection()
	{
		Vector3 dir = lightSource != null ? lightSource.transform.forward : lightDirection;
		return dir.sqrMagnitude > 1e-6f ? dir.normalized : Vector3.down;
	}

	// 用一台隐藏的正交相机沿光线方向渲一遍场景，拿到每束光的表面距离图
	private void Bake()
	{
		ReleaseBake();

		if (bakeSize.x <= 0f || bakeSize.z <= 0f)
		{
			Debug.LogError("[ScreenSpaceVolumetricLight] bakeSize 的 x/z 必须大于 0，烘焙范围为空，组件不生效", this);
			return;
		}

		Vector3 dir = ResolveDirection();
		Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up;
		float radius = Mathf.Max(0.5f * bakeSize.magnitude, 1f);

		bakeRT = new RenderTexture(bakeResolution, bakeResolution, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear)
		{
			name = "VolumetricLightOcclusion",
			filterMode = FilterMode.Point, // 双线性会把「无几何」的 0 与真实距离混成假表面
			wrapMode = TextureWrapMode.Clamp,
			hideFlags = HideFlags.HideAndDontSave,
		};
		bakeRT.Create();

		var go = new GameObject("VolumetricLightBakeCamera") { hideFlags = HideFlags.HideAndDontSave };
		bakeCam = go.AddComponent<Camera>();
		bakeCam.enabled = false;
		bakeCam.renderingPath = RenderingPath.Forward;
		bakeCam.orthographic = true;
		bakeCam.orthographicSize = radius;
		bakeCam.nearClipPlane = 0.01f;
		bakeCam.farClipPlane = radius * 2f;
		bakeCam.cullingMask = bakeLayers;
		bakeCam.clearFlags = CameraClearFlags.SolidColor;
		bakeCam.backgroundColor = Color.black;
		bakeCam.allowHDR = false;
		bakeCam.allowMSAA = false;
		bakeCam.useOcclusionCulling = false;
		bakeCam.depthTextureMode = DepthTextureMode.None;
		bakeCam.transform.SetPositionAndRotation(bakeCenter - dir * radius, Quaternion.LookRotation(dir, up));
		bakeCam.targetTexture = bakeRT;
		bakeCam.SetReplacementShader(bakeShader, bakeTag);
		bakeCam.Render();

		// uv 走投影矩阵、距离走视图矩阵，两者都取自这台相机，倾斜光线下同样成立
		// 渲染到纹理时 Unity 会翻 Y，uv 必须用同一套翻转后的投影矩阵，否则光柱会上下镜像
		lightVP = GL.GetGPUProjectionMatrix(bakeCam.projectionMatrix, true) * bakeCam.worldToCameraMatrix;
		lightV = bakeCam.worldToCameraMatrix;
		bakedDirection = dir;
	}

	public void Rebake()
	{
		if (raymarchMaterial == null) return;

		Bake();
		ready = bakeRT != null;
	}

	private void ReleaseBake()
	{
		if (bakeCam != null)
		{
			bakeCam.targetTexture = null;
			Destroy(bakeCam.gameObject);
			bakeCam = null;
		}

		if (bakeRT != null)
		{
			bakeRT.Release();
			Destroy(bakeRT);
			bakeRT = null;
		}
	}

	private void OnDestroy()
	{
		ReleaseBake();
		Destroy(raymarchMaterial);
		Destroy(compositeMaterial);
	}

#if UNITY_EDITOR
	private void Reset()
	{
		raymarchShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(RaymarchShaderPath);
		compositeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(CompositeShaderPath);
		bakeShader = UnityEditor.AssetDatabase.LoadAssetAtPath<Shader>(BakeShaderPath);

		if (raymarchShader == null || compositeShader == null || bakeShader == null)
			Debug.LogError($"[ScreenSpaceVolumetricLight] 找不到 shader，检查 {RaymarchShaderPath} / {CompositeShaderPath} / {BakeShaderPath}", this);
	}
#endif
}
