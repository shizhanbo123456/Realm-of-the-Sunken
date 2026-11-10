using UnityEngine;

public class EntityAnimData : MonoBehaviour
{
    public float height;                    // 身高
    public float legHeight;                 // 腿高

    [Header("武器挂点")]
    [Tooltip("是否在 hand_r 下建手上挂点；关掉则武器直挂 hand_r，不吃下面的位置偏移")]
    public bool useWeaponBone = true;
    [Tooltip("手上武器位置偏移（米），沿 hand_r 局部轴：x 拇指↔小指、y 手腕→指尖、z 掌心法线")]
    public Vector3 weaponHandLocalOffset = new Vector3(0f, 0.13f, -0.04f);

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
