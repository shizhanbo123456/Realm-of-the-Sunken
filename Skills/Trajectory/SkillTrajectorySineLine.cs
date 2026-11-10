using System;
using UnityEngine;

// 正弦直线：沿起点到终点的连线前进，同时在世界竖直方向做上下正弦振荡
public class SkillTrajectorySineLine : BulletTrajectory
{
    private readonly Vector3 start;
    private readonly Vector3 end;
    private readonly float amplitude;
    private readonly float cycles;
    private readonly float phase;

    public SkillTrajectorySineLine(Vector3 start, Vector3 end, float duration, float amplitude, float cycles, float phase)
    {
        if (duration <= 0f) throw new ArgumentOutOfRangeException(nameof(duration), duration, "时长必须 > 0");
        if (amplitude < 0f) throw new ArgumentOutOfRangeException(nameof(amplitude), amplitude, "振幅不能为负");
        if (cycles <= 0f) throw new ArgumentOutOfRangeException(nameof(cycles), cycles, "周期数必须 > 0");
        this.start = start;
        this.end = end;
        this.amplitude = amplitude;
        this.cycles = cycles;
        this.phase = phase;
        Duration = duration;
    }

    public override Vector3 Lerp(float factor)
    {
        float t = Mathf.Clamp01(factor);
        // 乘 t 保证出膛瞬间偏移为 0，振幅随飞行线性长到最大
        float wave = amplitude * Mathf.Sin(cycles * Mathf.PI * 2f * t + phase) * t;
        return Vector3.LerpUnclamped(start, end, t) + Vector3.up * wave;
    }
}
