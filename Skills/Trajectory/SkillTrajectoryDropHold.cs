using System;
using UnityEngine;

// 垂直下落：从高空沿直线砸到终点，抵达后原地驻留 hold 秒再销毁（终点可低于地表）
public class SkillTrajectoryDropHold : BulletTrajectory
{
    private readonly Vector3 start;
    private readonly Vector3 end;
    private readonly float fallFactor;

    public SkillTrajectoryDropHold(Vector3 start, Vector3 end, float fall, float hold)
    {
        if (fall <= 0f) throw new ArgumentOutOfRangeException(nameof(fall), fall, "下落时长必须 > 0");
        if (hold < 0f) throw new ArgumentOutOfRangeException(nameof(hold), hold, "终点驻留时长不能为负");
        this.start = start;
        this.end = end;
        Duration = fall + hold;
        fallFactor = fall / Duration;
    }

    public override Vector3 Lerp(float factor)
    {
        float f = Mathf.Clamp01(factor);
        if (f >= fallFactor) return end;
        return Vector3.LerpUnclamped(start, end, f / fallFactor);
    }

    public override Vector3 Start => start;
    public override Vector3 End => end;
}
