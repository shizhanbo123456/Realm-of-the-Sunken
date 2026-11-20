using UnityEngine;

// 匀减速直线位移：速度由初速线性降到末速，时长按实际行程推（平均速度 = 两端均值）
public class MotionDecelerate : MotionBase
{
    private readonly Vector3 dest;
    private readonly float startSpeed;
    private readonly float endSpeed;
    private Vector3 dir;
    private float startTime;
    private float duration;

    public MotionDecelerate(Vector3 dest, float startSpeed, float endSpeed)
    {
        this.dest = dest;
        this.startSpeed = startSpeed;
        this.endSpeed = endSpeed;
        canMove = false;
        endTime = float.MaxValue; // Enter 时按实际距离重算
    }

    public override Vector3 Enter(EntityData data, Vector3 speed)
    {
        Vector3 to = dest - data.transform.position;
        to.y = 0f;
        float dist = to.magnitude;
        dir = dist > 0.01f ? to / dist : data.transform.forward;
        duration = 2f * dist / Mathf.Max(0.01f, startSpeed + endSpeed);
        startTime = Time.time;
        endTime = startTime + duration;
        return dir * startSpeed;
    }

    public override Vector3 Update(EntityData data, Vector3 speed)
    {
        float t = duration > 0.0001f ? Mathf.Clamp01((Time.time - startTime) / duration) : 1f;
        return dir * Mathf.Lerp(startSpeed, endSpeed, t);
    }

    public override Vector3 Exit(EntityData data, Vector3 speed) => Vector3.zero; // 到点即停，不越过终点
}
