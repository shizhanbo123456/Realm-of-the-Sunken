using UnityEngine;

public class BulletPlayer : MonoBehaviour
{
    public enum RotationMode
    {
        Constant,
        Tangent,
        Camera,
        Identity,
        CompleteTangent
    }

    private float createTime;
    private float oneMinusLifetime;
    private BulletTrajectory trajectory;
    private RotationMode rotation;
    private void Init(BulletTrajectory trajectory, float lifeTime, RotationMode rotation)
    {
        createTime = Time.time;
        oneMinusLifetime = 1f / lifeTime;
        this.trajectory = trajectory;
        transform.position = trajectory.Lerp(0);
        this.rotation = rotation;
        if (rotation == RotationMode.Identity) transform.rotation = Quaternion.identity;
        else if (rotation == RotationMode.CompleteTangent) transform.LookAt(trajectory.End);
        else if (rotation == RotationMode.Tangent) FaceAhead(0f); // 立即应用初始切向，否则首帧是预制体默认朝向、下一帧才转正
        else if (rotation == RotationMode.Camera && Camera.main != null) transform.rotation = Camera.main.transform.rotation;
    }
    private void Update()
    {
        float f = (Time.time - createTime) * oneMinusLifetime;
        transform.position = trajectory.Lerp(f);
        if (rotation == RotationMode.Tangent)
        {
            FaceAhead(f);
        }
        else if (rotation == RotationMode.Camera)
        {
            transform.rotation = Camera.main.transform.rotation;
        }
    }
    // 轨迹完全不动时（定点子弹）前进方向为零向量，LookAt 会每帧报错；阈值取得极小，慢速爬行仍能定朝向
    private void FaceAhead(float factor)
    {
        Vector3 ahead = trajectory.Lerp(factor + 0.02f);
        if ((ahead - transform.position).sqrMagnitude > 1e-12f) transform.LookAt(ahead);
    }
    public static void Create(GameObject vfx, BulletTrajectory trajectory, float lifeTime = 0f, RotationMode rotation = RotationMode.Constant)
    {
        if (vfx == null || trajectory == null) return;
        float life = lifeTime > 0f ? lifeTime : trajectory.Duration; // 未传时长则用轨迹自带的
        BulletPlayer player;
        if (!vfx.TryGetComponent(out player))
        {
            player = vfx.AddComponent<BulletPlayer>();
        }
        player.Init(trajectory, life, rotation);
        Destroy(player.gameObject, life);
    }
}
