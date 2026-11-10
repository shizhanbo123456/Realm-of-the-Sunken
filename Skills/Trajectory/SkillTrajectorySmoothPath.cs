using System;
using System.Collections.Generic;
using UnityEngine;

// 平滑路径：从 startDir 出膛，依次穿过所有航点，按弧长匀速走完 duration
public class SkillTrajectorySmoothPath : BulletTrajectory
{
    private const float SampleStep = 0.15f;
    private const int MinSteps = 8;
    private const int MaxSteps = 48;

    private readonly Vector3[] waypoints;
    private readonly Vector3[] samples;
    private readonly float[] cumulative;

    public SkillTrajectorySmoothPath(Vector3[] points, Vector3 startDir, float duration, float tension = 1f)
    {
        if (points == null || points.Length < 2)
            throw new ArgumentException("平滑轨迹至少需要 2 个点", nameof(points));
        if (duration <= 0f)
            throw new ArgumentOutOfRangeException(nameof(duration), duration, "平滑轨迹时长必须 > 0");
        if (startDir.sqrMagnitude < 1e-8f)
            throw new ArgumentException("平滑轨迹需要非零的初始方向", nameof(startDir));
        if (tension <= 0f)
            throw new ArgumentOutOfRangeException(nameof(tension), tension, "张力必须 > 0");

        waypoints = Dedup(points);
        samples = Sample(waypoints, BuildTangents(waypoints, startDir.normalized, tension), out cumulative);
        Duration = duration;
    }

    public override Vector3 Start => waypoints[0];

    public override Vector3 End => waypoints[waypoints.Length - 1];

    public override Vector3 Lerp(float factor)
    {
        float target = Mathf.Clamp01(factor) * cumulative[cumulative.Length - 1];
        int lo = 0;
        int hi = cumulative.Length - 1;
        while (lo + 1 < hi)
        {
            int mid = (lo + hi) >> 1;
            if (cumulative[mid] <= target) lo = mid;
            else hi = mid;
        }
        float segment = cumulative[hi] - cumulative[lo];
        float t = segment <= 1e-6f ? 0f : (target - cumulative[lo]) / segment;
        return Vector3.LerpUnclamped(samples[lo], samples[hi], t);
    }

    private static Vector3[] Dedup(Vector3[] points)
    {
        var unique = new Vector3[points.Length];
        int count = 0;
        for (int i = 0; i < points.Length; i++)
        {
            if (count > 0 && (points[i] - unique[count - 1]).sqrMagnitude < 1e-8f) continue;
            unique[count++] = points[i];
        }
        if (count < 2)
            throw new ArgumentException("平滑轨迹去掉重复点后不足 2 个", nameof(points));
        if (count != points.Length) Array.Resize(ref unique, count);
        return unique;
    }

    // 首点切线由 startDir 指定（决定出膛方向），中间点取前后点差，末点沿用末段方向
    private static Vector3[] BuildTangents(Vector3[] p, Vector3 dir, float tension)
    {
        var tangents = new Vector3[p.Length];
        tangents[0] = dir * (Vector3.Distance(p[0], p[1]) * tension);
        for (int i = 1; i < p.Length - 1; i++)
            tangents[i] = (p[i + 1] - p[i - 1]) * (0.5f * tension);
        tangents[p.Length - 1] = (p[p.Length - 1] - p[p.Length - 2]) * tension;

        // 切线超过相邻弦长会让人字形打结，按 3 倍最短弦夹取
        for (int i = 0; i < p.Length; i++)
        {
            float prev = i > 0 ? Vector3.Distance(p[i - 1], p[i]) : float.MaxValue;
            float next = i < p.Length - 1 ? Vector3.Distance(p[i], p[i + 1]) : float.MaxValue;
            float limit = 3f * Mathf.Min(prev, next);
            if (tangents[i].sqrMagnitude > limit * limit)
                tangents[i] = tangents[i].normalized * limit;
        }
        return tangents;
    }

    private static Vector3[] Sample(Vector3[] p, Vector3[] m, out float[] cumulative)
    {
        var points = new List<Vector3>(p.Length * MinSteps) { p[0] };
        var lengths = new List<float>(p.Length * MinSteps) { 0f };
        for (int i = 0; i < p.Length - 1; i++)
        {
            int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(p[i], p[i + 1]) / SampleStep), MinSteps, MaxSteps);
            Vector3 last = p[i];
            float sum = lengths[lengths.Count - 1];
            for (int s = 1; s <= steps; s++)
            {
                Vector3 current = s == steps
                    ? p[i + 1]
                    : Hermite(p[i], m[i], p[i + 1], m[i + 1], s / (float)steps);
                sum += Vector3.Distance(last, current);
                points.Add(current);
                lengths.Add(sum);
                last = current;
            }
        }
        cumulative = lengths.ToArray();
        return points.ToArray();
    }

    private static Vector3 Hermite(Vector3 p0, Vector3 m0, Vector3 p1, Vector3 m1, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return (2f * t3 - 3f * t2 + 1f) * p0 + (t3 - 2f * t2 + t) * m0
            + (-2f * t3 + 3f * t2) * p1 + (t3 - t2) * m1;
    }
}
