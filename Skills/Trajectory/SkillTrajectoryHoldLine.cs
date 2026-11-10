using System;
using UnityEngine;

// 驻留直线：先在起点停住 hold 秒，再匀速直线飞到终点；驻留期间朝向由播放器按轨迹终点给出
public class SkillTrajectoryHoldLine : BulletTrajectory
{
    private readonly Vector3 start;
    private readonly Vector3 end;
    private readonly float holdFactor;

    public SkillTrajectoryHoldLine(Vector3 start, Vector3 end, float hold, float flight)
    {
        if (hold < 0f) throw new ArgumentOutOfRangeException(nameof(hold), hold, "驻留时长不能为负");
        if (flight <= 0f) throw new ArgumentOutOfRangeException(nameof(flight), flight, "飞行时长必须 > 0");
        this.start = start;
        this.end = end;
        Duration = hold + flight;
        holdFactor = hold / Duration;
    }

    public override Vector3 Lerp(float factor)
    {
        float f = Mathf.Clamp01(factor);
        if (f <= holdFactor) return start;
        return Vector3.LerpUnclamped(start, end, (f - holdFactor) / (1f - holdFactor));
    }

    public override Vector3 Start => start;

    public override Vector3 End => end;
}
