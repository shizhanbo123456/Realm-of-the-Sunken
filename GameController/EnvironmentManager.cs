using Ros.Transport;
using UnityEngine;

using Ros.Config;
public class EnvironmentManager : MonoBehaviour
{
    public const float CycleLength = 2f;

    public const float DayNightBoundary = 0.5f;

    public const float SkyExposureMidnight = 0.28f;
    public const float SkyExposureNoon = 1.92f;
    public const string SkyExposureProperty = "_Exposure";

    public const float SunIntensityMax = 1.5f;

    // 昼夜时长（秒）：白天 = 光照值 ≥ 0.5 区间总耗时，晚上 = 光照值 < 0.5 区间总耗时。
    // 不序列化：唯一来源是 global_variables 的 day_duration / night_duration（GM 可在运行时改写并随快照下发）
    [System.NonSerialized] public float dayDuration;
    [System.NonSerialized] public float nightDuration;

    [Header("表现引用（两套场景各自挂自己那份；服务器可不填）")]
    [Tooltip("天空盒材质球：昼夜变化时线性调整其 " + SkyExposureProperty + "（午夜 0.28 ~ 正午 1.92）")]
    public Material skyboxMaterial;

    [Header("雾气（距离雾）")]
    [Tooltip("是否起雾：无雾 = 距离参数 1，起雾 = fogDistanceScale；切换时按 fogTransitionDuration 秒线性过渡。（客户端由「迷雾」Buff 自动驱动，此处只是初始值）")]
    public bool fogEnabled = false;

    [Tooltip("起雾时 ScreenSpaceFog 距离参数的目标值，越小雾越近越浓")]
    [Min(0.01f)] public float fogDistanceScale = 0.35f;

    [Tooltip("雾效开关的过渡时间（秒）")]
    [Min(0.01f)] public float fogTransitionDuration = 10f;

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

    private float fogDistanceCurrent;
    private ScreenSpaceFog cachedFog;
    private bool fogComponentWarned;
    private int skyExposureId;
    private bool exposureWarningLogged;

    private void Awake()
    {
        Tool.EnvironmentManager = this;
        ApplyConfigDurations();
        skyExposureId = Shader.PropertyToID(SkyExposureProperty);
        lastDayDuration = dayDuration;
        lastNightDuration = nightDuration;
        fogDistanceCurrent = fogEnabled ? fogDistanceScale : 1f;
        RenderSettings.fog = false;
        ApplyVisual();
    }

    // 昼夜时长只来自配表 global_variables，代码里不设默认值
    private void ApplyConfigDurations()
    {
        int day = GlobalVariable.I(global_variablesEnum.day_duration);
        int night = GlobalVariable.I(global_variablesEnum.night_duration);

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
            // 非对局中不推进昼夜、不显示太阳（首页/大厅各有专用光照），但雾效开关的过渡仍要能走完
            ApplySunVisible();
            ApplyFogDistance();
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
        ApplySunVisible();
        ApplyFogDistance();
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

    // 平行光只属于战斗：首页/大厅各有专用光照，非对局一律不显示
    private void ApplySunVisible()
    {
        if (sun == null || BattleManager.AtServer) return;
        bool visible = NetworkManager.BattleRunning;
        if (sun.enabled != visible) sun.enabled = visible;
    }

    // 起雾时拉近距离雾起止距离，1 ↔ fogDistanceScale 按 fogTransitionDuration 秒过渡
    private void ApplyFogDistance()
    {
        if (BattleManager.AtServer) return;

        float target = fogEnabled ? fogDistanceScale : 1f;
        if (!Mathf.Approximately(fogDistanceCurrent, target))
        {
            float speed = Mathf.Abs(fogDistanceScale - 1f) / Mathf.Max(0.01f, fogTransitionDuration);
            fogDistanceCurrent = Mathf.MoveTowards(fogDistanceCurrent, target, speed * Time.deltaTime);
        }

        if (cachedFog == null)
        {
            Camera mainCam = Camera.main;
            cachedFog = mainCam != null ? mainCam.GetComponent<ScreenSpaceFog>() : null;
            if (cachedFog == null)
            {
                if (!fogComponentWarned)
                {
                    fogComponentWarned = true;
                    Debug.LogError("[EnvironmentManager] MainCamera 上未挂 ScreenSpaceFog，雾气效果不生效", this);
                }
                return;
            }
        }
        cachedFog.distanceScale = fogDistanceCurrent;
    }
}
