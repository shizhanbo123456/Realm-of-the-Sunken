using UnityEngine;

// 挂在体积光 + Area Light 的物体上：锁死世界朝向，并把体积中心贴在主相机前方半个 Range 处
[DefaultExecutionOrder(90)]
[DisallowMultipleComponent]
public class VolumetricAreaLightFollow : MonoBehaviour
{
    [Tooltip("运行时锁定的世界旋转（欧拉角）")]
    public Vector3 eulerAngles;

    [Tooltip("留空则自动取同物体上的 Light")]
    public Light areaLight;

    Camera cam;

    void Start()
    {
        if (areaLight == null) areaLight = GetComponent<Light>();
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;      // 取到一次后不再查询
        if (cam == null || areaLight == null) return;

        transform.rotation = Quaternion.Euler(eulerAngles);
        transform.position = cam.transform.position - transform.forward * (areaLight.range * 0.5f);
    }
}
