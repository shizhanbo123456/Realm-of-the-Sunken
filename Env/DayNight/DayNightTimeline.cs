using System.Collections.Generic;
using UnityEngine;

public struct DayNightValues
{
    public float sunIntensity;
    public Quaternion sunRotation;
    public float causticsIntensity;
    public Color causticsColor;
    public Quaternion volumetricLightRotation;
    public Color volumetricLightAlbedo;
    public float volumetricLightBrightness;
    public Color fogColorTop;
    public Color fogColorBottom;
    public Color waterColor;
    public Color waterDeepColor;
}

// 按时间戳循环采样：0 与 2 等价，最后一帧与第一帧之间也按循环区间插值
public sealed class DayNightTimeline
{
    public const float CycleLength = 2f;

    private readonly List<DayNightProfile> frames = new();

    public int Count => frames.Count;

    public void Rebuild(IReadOnlyList<DayNightProfile> source)
    {
        if (source == null || source.Count == 0)
            throw new System.InvalidOperationException("[昼夜] 参数帧列表为空，至少需要 1 个 DayNightProfile");

        frames.Clear();
        for (int i = 0; i < source.Count; i++)
        {
            DayNightProfile profile = source[i];
            if (profile == null)
                throw new System.InvalidOperationException($"[昼夜] 参数帧列表第 {i} 项为空引用");
            InsertSorted(profile);
        }

        if (frames.Count < 2) return;
        for (int i = 0; i < frames.Count; i++)
        {
            int next = (i + 1) % frames.Count;
            if (Mathf.Approximately(Norm(frames[i].time), Norm(frames[next].time)))
                throw new System.InvalidOperationException($"[昼夜] 时间戳重复：{Norm(frames[i].time)}（0 与 2 视为同一时刻）");
        }
    }

    public DayNightValues Sample(float time)
    {
        if (frames.Count == 0)
            throw new System.InvalidOperationException("[昼夜] 采样前必须先 Rebuild");

        if (frames.Count == 1) return Combine(frames[0], frames[0], 0f);

        float t = Norm(time);

        int i = -1;
        for (int k = 0; k < frames.Count; k++)
        {
            if (Norm(frames[k].time) <= t) i = k;
            else break;
        }

        int j;
        float t0, t1, x;
        if (i < 0)
        {
            // t 落在最后一帧与第一帧之间（跨午夜）
            i = frames.Count - 1;
            j = 0;
            t0 = Norm(frames[i].time);
            t1 = Norm(frames[j].time) + CycleLength;
            x = t + CycleLength;
        }
        else
        {
            j = (i + 1) % frames.Count;
            t0 = Norm(frames[i].time);
            t1 = j == 0 ? Norm(frames[j].time) + CycleLength : Norm(frames[j].time);
            x = t;
        }

        float span = t1 - t0;
        if (span <= 0f)
            throw new System.InvalidOperationException($"[昼夜] 相邻帧无法构成区间：{t0} → {t1}");

        return Combine(frames[i], frames[j], (x - t0) / span);
    }

    private static float Norm(float time) => Mathf.Repeat(time, CycleLength);

    private void InsertSorted(DayNightProfile profile)
    {
        float t = Norm(profile.time);
        int i = frames.Count;
        while (i > 0 && Norm(frames[i - 1].time) > t) i--;
        frames.Insert(i, profile);
    }

    private static DayNightValues Combine(DayNightProfile a, DayNightProfile b, float w)
    {
        return new DayNightValues
        {
            sunIntensity = Mathf.Lerp(a.sunIntensity, b.sunIntensity, w),
            sunRotation = Quaternion.Slerp(Quaternion.Euler(a.sunEulerAngles), Quaternion.Euler(b.sunEulerAngles), w),
            causticsIntensity = Mathf.Lerp(a.causticsIntensity, b.causticsIntensity, w),
            causticsColor = Color.Lerp(a.causticsColor, b.causticsColor, w),
            volumetricLightRotation = Quaternion.Slerp(Quaternion.Euler(a.volumetricLightEulerAngles), Quaternion.Euler(b.volumetricLightEulerAngles), w),
            volumetricLightAlbedo = Color.Lerp(a.volumetricLightAlbedo, b.volumetricLightAlbedo, w),
            volumetricLightBrightness = Mathf.Lerp(a.volumetricLightBrightness, b.volumetricLightBrightness, w),
            fogColorTop = Color.Lerp(a.fogColorTop, b.fogColorTop, w),
            fogColorBottom = Color.Lerp(a.fogColorBottom, b.fogColorBottom, w),
            waterColor = Color.Lerp(a.waterColor, b.waterColor, w),
            waterDeepColor = Color.Lerp(a.waterDeepColor, b.waterDeepColor, w),
        };
    }
}
