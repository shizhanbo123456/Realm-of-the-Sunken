using Ros.Config;
using Ros.Transport;
using UnityEngine;
using UnityEngine.Profiling;

// 昼夜时钟：两端各自推演，服务器广播快照纠偏；表现层只读 CycleTime
public partial class BattleManager
{
    public const float DayNightCycleLength = 2f;
    public const float DayNightBoundary = 0.5f;

    // 0/2 午夜、1 正午
    public static float CycleTime { get; private set; } = 1f;

    // 时间行进方向：+1 走向正午、-1 走向午夜；只在昼夜推演内部使用
    private static int Direction => CycleTime < 1f ? 1 : -1;

    // 白天程度：0 = 午夜、1 = 正午，一整天会 0→1→0 折返，同一个值对应上行/下行两个时刻
    // 只服务 IsDay / DayFactor；对外要"时间"一律读 CycleTime（0~2 单调），拿它当进度会来回跳
    private static float Time01 => 1f - Mathf.Abs(CycleTime - 1f);

    public static bool IsDay => Time01 >= DayNightBoundary;

    public static int DayNightState => IsDay ? 1 : 0;

    public static float DayFactor => Mathf.Clamp01((Time01 - DayNightBoundary) / DayNightBoundary);

    public static event System.Action<bool> DayNightFlipped;

    [System.NonSerialized] public float dayDuration;
    [System.NonSerialized] public float nightDuration;

    private float lastDayDuration;
    private float lastNightDuration;
    private bool dayNightSyncRequested;

    // 时长唯一来源是 global_variables，代码里不设默认值
    private void ApplyConfigDurations()
    {
        int day = GlobalVariable.I(global_variablesEnum.day_duration);
        int night = GlobalVariable.I(global_variablesEnum.night_duration);

        if (day <= 0 || night <= 0)
            throw new System.InvalidOperationException("[配表] global_variables.csv 的 day_duration / night_duration 必须填大于 0 的整数");

        dayDuration = day;
        nightDuration = night;
        lastDayDuration = day;
        lastNightDuration = night;
    }

    // ManagedUpdate 早退前调用：客户端本地推演，服务器权威推进并广播
    private void UpdateDayNight()
    {
        // 客户端从不置 BattleStarted，只能看组队快照里的 battleStarted
        bool running = AtServer ? BattleStarted : NetworkManager.BattleRunning;
        if (!running) return;

        Profiler.BeginSample(kDayNightTag);

        bool wasDay = IsDay;

        // 速率 = 1 / 当前区间时长：改时长只变速，不改当前时刻
        float duration = wasDay ? dayDuration : nightDuration;
        CycleTime += UnityEngine.Time.deltaTime / duration;
        CycleTime %= DayNightCycleLength;
        if (CycleTime < 0f) CycleTime += DayNightCycleLength;

        if (!AtServer) Tool.EnvironmentManager.SetTime(CycleTime);

        NotifyDayNightFlipped(wasDay);

        // 时长被外部改动 → 置位同步请求，由服务器补发完整快照
        if (dayDuration != lastDayDuration || nightDuration != lastNightDuration)
        {
            lastDayDuration = dayDuration;
            lastNightDuration = nightDuration;
            dayNightSyncRequested = true;
        }

        if (AtServer && (ConsumeDayNightSyncRequest() || dayNightTicker))
        {
            dayNightTicker.Reset();
            Tool.NetworkManager.SendDayNightInfo(BuildSnapshot());
        }

        Profiler.EndSample(); // kDayNightTag
    }

    public void SetCycleTime(float cycleTime)
    {
        bool wasDay = IsDay;
        CycleTime = Mathf.Repeat(cycleTime, DayNightCycleLength);
        if (!AtServer) Tool.EnvironmentManager.SetTime(CycleTime);
        NotifyDayNightFlipped(wasDay);
        dayNightSyncRequested = true;
    }

    public void ResetDayNight()
    {
        bool wasDay = IsDay;
        CycleTime = 1f;
        if (!AtServer) Tool.EnvironmentManager.SetTime(CycleTime);
        NotifyDayNightFlipped(wasDay);
    }

    public void ApplyDayNightSync(float cycleTime, float day, float night)
    {
        bool wasDay = IsDay;
        dayDuration = day;
        nightDuration = night;
        lastDayDuration = dayDuration;
        lastNightDuration = nightDuration;
        CycleTime = Mathf.Repeat(cycleTime, DayNightCycleLength);
        if (!AtServer) Tool.EnvironmentManager.SetTime(CycleTime);
        NotifyDayNightFlipped(wasDay);
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

    public bool ConsumeDayNightSyncRequest()
    {
        bool requested = dayNightSyncRequested;
        dayNightSyncRequested = false;
        return requested;
    }

    private void NotifyDayNightFlipped(bool wasDay)
    {
        if (wasDay == IsDay) return;
        EventManager.TrigEvent(ClientEvent.OnDayNightChange, DayNightState);
        if (AtServer && DayNightFlipped != null) DayNightFlipped.Invoke(IsDay);
    }
}
