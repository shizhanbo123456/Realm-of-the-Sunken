using UnityEngine;

// 昼夜参数帧：一个资产代表时间轴 0~2 上的一个时间戳（0/2 午夜、1 正午）
[CreateAssetMenu(fileName = "DayNightProfile", menuName = "DayNightProfile")]
public class DayNightProfile : ScriptableObject
{
    [Range(0f, 2f)] public float time = 1f;

    [Header("太阳（平行光）")]
    [Min(0f)] public float sunIntensity = 1.5f;
    public Vector3 sunEulerAngles = new Vector3(50f, -30f, 0f);

    [Header("焦散")]
    [Min(0f)] public float causticsIntensity = 1f;
    public Color causticsColor = Color.white;

    [Header("体积光")]
    [Tooltip("写入 VolumetricAreaLightFollow.eulerAngles")]
    public Vector3 volumetricLightEulerAngles = new Vector3(50f, -30f, 0f);
    public Color volumetricLightAlbedo = Color.white;
    [Min(0f)] public float volumetricLightBrightness = 1f;

    [Header("雾色（fogColorEdge 恒为黑，不参与）")]
    public Color fogColorTop = new Color(0.36f, 0.45f, 0.49f);
    public Color fogColorBottom = new Color(0.17f, 0.22f, 0.25f);
    public Color waterColor = new Color(0.36f, 0.50f, 0.54f);
    public Color waterDeepColor = new Color(0.21f, 0.29f, 0.33f);
}
