using UnityEngine;

// 登场下落：从锚点上方匀速落到锚点，落地后移除自身（首页/大厅展示用）
public class SpawnDrop : MonoBehaviour
{
    private Vector3 from;
    private Vector3 target;
    private float duration;
    private float elapsed;

    public void Play(Vector3 to, float height, float fallDuration)
    {
        target = to;
        from = to + Vector3.up * height;
        duration = fallDuration;
        elapsed = 0f;
        transform.position = from;
    }

    // 视图被重新摆位时立即落地并结束，避免继续拽着位置
    public void SnapToTarget()
    {
        transform.position = target;
        Destroy(this);
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float t = elapsed / duration;
        transform.position = Vector3.LerpUnclamped(from, target, t);
        if (t >= 1f) SnapToTarget();
    }
}
