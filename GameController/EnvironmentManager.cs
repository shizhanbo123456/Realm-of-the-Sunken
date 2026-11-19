using System.Collections.Generic;
using UnityEngine;
using VolumetricLights;

// 昼夜表现层：只按外部设置的 0~2 时间应用画面参数，不推进时间、不感知游戏阶段
public class EnvironmentManager : MonoBehaviour
{
    public const float SunEnableThreshold = 0.01f;

    [Header("昼夜参数帧（一个资产一个时间戳，0/2 午夜、1 正午）")]
    public List<DayNightProfile> profiles = new();

    [Header("时间（0~2，由外部设置）")]
    [Range(0f, 2f)] public float time = 1f;

    [Header("场景组件（自动收集）")]
    public Light sun;
    public ScreenSpaceFog fog;
    public ScreenSpaceCaustics caustics;
    public VolumetricLight volumetricLight;
    public VolumetricAreaLightFollow volumetricFollow;

    [Header("雾距离（由「迷雾」Buff 驱动，非昼夜）")]
    public bool fogEnabled = false;
    [Min(0.01f)] public float fogDistanceScale = 0.35f;
    [Min(0.01f)] public float fogTransitionDuration = 10f;

    private readonly DayNightTimeline timeline = new();
    private float fogDistanceCurrent;
    private float appliedTime = float.NaN;
    private bool ready;

    private void Awake()
    {
        Tool.EnvironmentManager = this;
    }

    private void Start()
    {
        CollectComponents();
        ValidateComponents();
        fogDistanceCurrent = fogEnabled ? fogDistanceScale : 1f;
        Apply(time);
        ready = true;
    }

    private void Update()
    {
        if (!ready) return;

        ApplyFogDistance();
        caustics.SetRenderEnabled(ClientSettings.Caustics); // 设置面板的焦散开关：关着时连全屏绘制都不提交
        if (!Mathf.Approximately(time, appliedTime)) Apply(time);
    }

    public void SetTime(float value)
    {
        time = Mathf.Repeat(value, DayNightTimeline.CycleLength);
    }

    // 面板预览：立即按给定时间应用（编辑模式同样生效）
    public void ApplyTime(float value)
    {
        time = Mathf.Repeat(value, DayNightTimeline.CycleLength);
        Apply(time);
    }

    public void SetFogEnabled(bool enabled)
    {
        fogEnabled = enabled;
    }

    public void CollectComponents()
    {
        sun = FindSun();
        Camera cam = Camera.main;
        fog = cam != null ? cam.GetComponent<ScreenSpaceFog>() : null;
        caustics = cam != null ? cam.GetComponent<ScreenSpaceCaustics>() : null;
        volumetricLight = FindVolumetricLight();
        volumetricFollow = volumetricLight != null ? volumetricLight.GetComponent<VolumetricAreaLightFollow>() : null;
    }

    private void Apply(float value)
    {
        timeline.Rebuild(profiles);
        DayNightValues v = timeline.Sample(value);

        sun.intensity = v.sunIntensity;
        sun.enabled = v.sunIntensity > SunEnableThreshold;
        sun.transform.rotation = v.sunRotation;

        caustics.intensity = v.causticsIntensity;
        caustics.color = v.causticsColor;

        volumetricFollow.eulerAngles = v.volumetricLightRotation.eulerAngles;
        volumetricLight.mediumAlbedo = v.volumetricLightAlbedo;
        volumetricLight.brightness = v.volumetricLightBrightness;

        fog.fogColorTop = v.fogColorTop;
        fog.fogColorBottom = v.fogColorBottom;
        fog.waterColor = v.waterColor;
        fog.waterDeepColor = v.waterDeepColor;

        appliedTime = value;
    }

    public void ValidateComponents()
    {
        var missing = new List<string>();
        if (sun == null) missing.Add("平行光（LightType.Directional）");
        if (fog == null) missing.Add("MainCamera 上的 ScreenSpaceFog");
        if (caustics == null) missing.Add("MainCamera 上的 ScreenSpaceCaustics");
        if (volumetricLight == null) missing.Add("VolumetricLight");
        if (volumetricFollow == null) missing.Add("VolumetricLight 同物体上的 VolumetricAreaLightFollow");
        if (missing.Count > 0)
            throw new System.InvalidOperationException("[EnvironmentManager] 场景缺少必须组件：\n - " + string.Join("\n - ", missing));
    }

    private static Light FindSun()
    {
        Light found = null;
        foreach (Light light in FindObjectsOfType<Light>())
        {
            if (light.type != LightType.Directional) continue;
            if (found != null)
                throw new System.InvalidOperationException("[EnvironmentManager] 场景中存在多个平行光，无法确定太阳");
            found = light;
        }
        return found;
    }

    private static VolumetricLight FindVolumetricLight()
    {
        VolumetricLight found = null;
        foreach (VolumetricLight light in FindObjectsOfType<VolumetricLight>())
        {
            if (found != null)
                throw new System.InvalidOperationException("[EnvironmentManager] 场景中存在多个 VolumetricLight，无法确定体积光");
            found = light;
        }
        return found;
    }

    // 起雾时拉近距离雾起止距离，1 ↔ fogDistanceScale 按 fogTransitionDuration 秒过渡
    private void ApplyFogDistance()
    {
        float target = fogEnabled ? fogDistanceScale : 1f;
        if (!Mathf.Approximately(fogDistanceCurrent, target))
        {
            float speed = Mathf.Abs(fogDistanceScale - 1f) / Mathf.Max(0.01f, fogTransitionDuration);
            fogDistanceCurrent = Mathf.MoveTowards(fogDistanceCurrent, target, speed * Time.deltaTime);
        }
        fog.distanceScale = fogDistanceCurrent;
    }
}
