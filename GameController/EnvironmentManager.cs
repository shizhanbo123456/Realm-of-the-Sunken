using Ros.Transport;
using UnityEngine;

public class EnvironmentManager : MonoBehaviour
{
    public const float CycleLength = 2f;

    public const float DayNightBoundary = 0.5f;

    public const float SkyExposureMidnight = 0.28f;
    public const float SkyExposureNoon = 1.92f;
    public const string SkyExposureProperty = "_Exposure";

    public const float SunIntensityMax = 1.5f;

    public const float FogDensityClear = 0.08f;
    public const float FogDensityFogged = 0.5f;

    // 昼夜时长（秒）：白天 = 光照值 ≥ 0.5 区间总耗时，晚上 = 光照值 < 0.5 区间总耗时。
    // 不序列化：唯一来源是 global_variables 的 day_duration / night_duration（GM 可在运行时改写并随快照下发）
    [System.NonSerialized] public float dayDuration;
    [System.NonSerialized] public float nightDuration;

    [Header("表现引用（两套场景各自挂自己那份；服务器可不填）")]
    [Tooltip("天空盒材质球：昼夜变化时线性调整其 " + SkyExposureProperty + "（午夜 0.28 ~ 正午 1.92）")]
    public Material skyboxMaterial;

    [Header("雾气（Unity 内置雾）")]
    [Tooltip("是否起雾：无雾 = Density 0.08，起雾 = Density 0.5；切换时按 fogTransitionDuration 秒线性过渡。（客户端由「迷雾」Buff 自动驱动，此处只是初始值）")]
    public bool fogEnabled = false;

    [Tooltip("雾效开关的过渡时间（秒）")]
    [Min(0.01f)] public float fogTransitionDuration = 10f;

    [Tooltip("雾色（RenderSettings.fogColor）在正午的颜色（8bit 64,82,89）；凌晨 / 傍晚（及夜间）线性衰减为黑")]
    public Color fogAlbedoNoon = new Color(64f / 255f, 82f / 255f, 89f / 255f, 1f);

    [Header("太阳")]
    [Tooltip("太阳（平行光）：强度由昼夜时间驱动；朝向固定为场景默认角度，不随时间旋转")]
    public Light sun;

    public static float CycleTime { get; private set; } = 1f;

    // 光照方向（+1 变亮 / -1 变暗），不可用作时间推进方向
    public static int Direction => CycleTime < 1f ? 1 : -1;

    public static float Time01 => 1f - Mathf.Abs(CycleTime - 1f);

    public static bool IsDay => Time01 >= DayNightBoundary;

    public static int State => IsDay ? 1 : 0;

    public static float DayFactor => Mathf.Clamp01((Time01 - DayNightBoundary) / DayNightBoundary);

    public static event System.Action<bool> DayNightFlipped;

    private float lastDayDuration;
    private float lastNightDuration;
    private bool syncRequested;

    private float fogDensityCurrent;
    private int skyExposureId;
    private bool exposureWarningLogged;

    private void Awake()
    {
        Tool.EnvironmentManager = this;
        ApplyConfigDurations();
        skyExposureId = Shader.PropertyToID(SkyExposureProperty);
        lastDayDuration = dayDuration;
        lastNightDuration = nightDuration;
        fogDensityCurrent = fogEnabled ? FogDensityFogged : FogDensityClear;
        // 内置雾常开，浓淡只由 fogDensity 表达（指数平方 = Unity 默认雾模式）
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        ApplyVisual();
    }

    // 昼夜时长只来自配表 global_variables，代码里不设默认值
    private void ApplyConfigDurations()
    {
        int day = ConfigManager.GlobalVariableConfig.day_duration;
        int night = ConfigManager.GlobalVariableConfig.night_duration;

        if (day <= 0 || night <= 0)
            throw new System.InvalidOperationException("[配表] global_variables.csv 的 day_duration / night_duration 必须填大于 0 的整数");

        dayDuration = day;
        nightDuration = night;
    }

    private void Update()
    {
        if (BattleManager.AtServer) return;
        if (!NetworkManager.BattleRunning)
        {
            // 非对局中不推进昼夜，但雾效开关的过渡仍要能走完（允许在组队大厅切换雾效）
            ApplyFogDensity();
            return;
        }
        Tick(Time.deltaTime);
    }

    public bool Tick(float deltaTime)
    {
        bool wasDay = IsDay;

        // 速率 = 1 / 当前区间时长（倒数）：改时长只变速，不改当前时间
        float duration = wasDay ? dayDuration : nightDuration;
        CycleTime += deltaTime / duration; // 恒正向推进；按光照方向折返会锁死在正午

        // 周期回绕：[0, 2) 循环（0 与 2 都是午夜）
        CycleTime %= CycleLength;
        if (CycleTime < 0f) CycleTime += CycleLength;

        bool flipped = wasDay != IsDay;
        ApplyVisual();
        NotifyFlipped(wasDay);

        // 外部改了任一昼夜时长 → 置位同步请求，由服务器补发完整快照
        if (dayDuration != lastDayDuration || nightDuration != lastNightDuration)
        {
            lastDayDuration = dayDuration;
            lastNightDuration = nightDuration;
            syncRequested = true;
        }

        return flipped;
    }

    public void ApplyServerSync(float cycleTime, float dayDuration, float nightDuration)
    {
        bool wasDay = IsDay;
        this.dayDuration = dayDuration;
        this.nightDuration = nightDuration;
        lastDayDuration = this.dayDuration;
        lastNightDuration = this.nightDuration;
        CycleTime = Mathf.Repeat(cycleTime, CycleLength);
        ApplyVisual();
        NotifyFlipped(wasDay);
    }

    public void SetCycleTime(float cycleTime)
    {
        bool wasDay = IsDay;
        CycleTime = Mathf.Repeat(cycleTime, CycleLength);
        ApplyVisual();
        NotifyFlipped(wasDay);
        syncRequested = true;
    }

    public void SetFogEnabled(bool enabled)
    {
        fogEnabled = enabled;
    }

    public void ResetDayNight()
    {
        bool wasDay = IsDay;
        CycleTime = 1f;
        ApplyVisual();
        NotifyFlipped(wasDay);
    }

    public SCDayNightInfo BuildSnapshot()
    {
        return new SCDayNightInfo()
        {
            cycleTime = CycleTime,
            dayDuration = dayDuration,
            nightDuration = nightDuration,
        };
    }

    public bool ConsumeSyncRequest()
    {
        bool requested = syncRequested;
        syncRequested = false;
        return requested;
    }

    private void ApplyVisual()
    {
        ApplySkyboxExposure();
        ApplySun();
        ApplyFogColor();
        ApplyFogDensity();
    }

    private void NotifyFlipped(bool wasDay)
    {
        if (wasDay == IsDay) return;
        EventManager.TrigEvent(ClientEvent.OnDayNightChange, State);
        if (BattleManager.AtServer && DayNightFlipped != null) DayNightFlipped.Invoke(IsDay);
    }

    private void ApplySkyboxExposure()
    {
        if (skyboxMaterial == null) return;
        if (!skyboxMaterial.HasProperty(skyExposureId))
        {
            if (!exposureWarningLogged)
            {
                exposureWarningLogged = true;
                Debug.LogWarning($"[EnvironmentManager] 天空盒材质 {skyboxMaterial.name} 没有 {SkyExposureProperty} 属性，曝光不会被调整。");
            }
            return;
        }
        skyboxMaterial.SetFloat(skyExposureId, Mathf.Lerp(SkyExposureMidnight, SkyExposureNoon, Time01));
    }

    // 只改强度：朝向保持场景里摆好的默认角度，昼夜推进不旋转
    private void ApplySun()
    {
        if (sun == null) return;
        sun.intensity = DayFactor * SunIntensityMax;
    }

    private void ApplyFogColor()
    {
        Color target = fogAlbedoNoon * DayFactor;
        target.a = 1f;
        if (RenderSettings.fogColor != target) RenderSettings.fogColor = target;
    }

    // 雾浓淡：无雾 0.08 ↔ 起雾 0.5，按 fogTransitionDuration 秒线性过渡
    private void ApplyFogDensity()
    {
        float target = fogEnabled ? FogDensityFogged : FogDensityClear;
        if (!Mathf.Approximately(fogDensityCurrent, target))
        {
            float speed = (FogDensityFogged - FogDensityClear) / Mathf.Max(0.01f, fogTransitionDuration);
            fogDensityCurrent = Mathf.MoveTowards(fogDensityCurrent, target, speed * Time.deltaTime);
        }
        if (!Mathf.Approximately(RenderSettings.fogDensity, fogDensityCurrent))
            RenderSettings.fogDensity = fogDensityCurrent;
    }
}
