using UnityEngine;

public class EntityAnimData : MonoBehaviour
{
    public float height;                    // 身高
    public float legHeight;                 // 腿高
    [Tooltip("收起武器挂点：长枪用这里")]
    public Transform weaponBackMount;
    [Tooltip("收起武器挂点：刀用这里，双刀的右手那把也用这里")]
    public Transform weaponHipRightMount;
    [Tooltip("收起武器挂点：双刀的左手那把")]
    public Transform weaponHipLeftMount;

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.5f);
        Gizmos.DrawWireCube(transform.position + Vector3.up * height * 0.5f, new Vector3(0.5f, height, 0.5f));
        Gizmos.color = new Color(0.9f, 0.7f, 0.3f, 0.5f);
        Gizmos.DrawCube(transform.position + Vector3.up * legHeight * 0.5f, new Vector3(0.3f, legHeight, 0.3f));
    }

    public float RunSpeed => legHeight * 7f;
    private float JumpHeight => legHeight * 0.8f;
    public float JumpSpeed => Mathf.Sqrt(20 * JumpHeight);
}
