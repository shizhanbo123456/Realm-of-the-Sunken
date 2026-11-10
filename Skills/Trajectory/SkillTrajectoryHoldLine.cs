using System;
using UnityEngine;

// 驻留直线：驻留段沿飞行方向以 1m/s 缓慢爬行（切向不能为零），随后匀速直线飞到终点
public class SkillTrajectoryHoldLine : BulletTrajectory
{
    private const float CreepSpeed = 1f;

    private readonly Vector3 start;
    private readonly Vector3 end;
    private readonly Vector3 creep;
    private readonly float hold;
    private readonly float holdFactor;

    public SkillTrajectoryHoldLine(Vector3 start, Vector3 end, float hold, float flight)
    {
        if (hold < 0f) throw new ArgumentOutOfRangeException(nameof(hold), hold, "驻留时长不能为负");
        if (flight <= 0f) throw new ArgumentOutOfRangeException(nameof(flight), flight, "飞行时长必须 > 0");
        this.start = start;
        this.end = end;
        this.hold = hold;
        creep = (end - start).normalized * CreepSpeed; // 起终点重合时 normalized 返回零向量，不会出 NaN
        Duration = hold + flight;
        holdFactor = hold / Duration;
    }

    public override Vector3 Lerp(float factor)
    {
        float f = Mathf.Clamp01(factor);
        if (f <= holdFactor) return start + creep * (f * Duration);
        return Vector3.LerpUnclamped(start + creep * hold, end, (f - holdFactor) / (1f - holdFactor));
    }

    public override Vector3 Start => start;

    public override Vector3 End => end;
}
