using UnityEngine;

namespace TerrainGen
{
    // 只保留生成数据标识，其余生成参数见 StreamingAssets/ConfigData/terrain_generation.csv
    [CreateAssetMenu(menuName = "模块化地形/建筑生成参数", fileName = "BuildingSettings")]
    public class BuildingSettings : ScriptableObject
    {
        [Tooltip("随机种子：种子 + 版本号 一起决定烘焙文件的路径")]
        public int seed = 20261105;

        [Tooltip("生成数据版本号：改过生成逻辑或素材表后 +1")]
        [Min(1)] public int version = 1;
    }
}
