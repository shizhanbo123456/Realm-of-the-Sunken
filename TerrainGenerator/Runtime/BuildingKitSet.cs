using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    [CreateAssetMenu(menuName = "模块化地形/装饰素材表", fileName = "BuildingKitSet")]
    public class BuildingKitSet : ScriptableObject
    {
        [Header("地面（每格一块，顶面对齐格面）")]
        public List<GameObject> ground = new List<GameObject>();

        [Header("围栏（沿边界边铺设）")]
        public List<GameObject> fences = new List<GameObject>();

        [Header("矮石柱（放在每段栏杆两端的格角）")]
        public List<GameObject> shortPosts = new List<GameObject>();

        [Header("高石柱（同上，且下方会生成支撑）")]
        public List<GameObject> tallPosts = new List<GameObject>();

        [Header("支撑（放在高石柱下方，原点为支撑顶部中心）")]
        public List<GameObject> supports = new List<GameObject>();

        [Header("装饰（按面积密度随机摆放，底面对齐地面）")]
        public List<GameObject> decorations = new List<GameObject>();

        [Header("藤蔓（贴在地块临空外墙侧面，高度随机）")]
        public List<GameObject> vines = new List<GameObject>();

        [Header("障碍（放在无地面格，允许压地面但不能整块落在地面上）")]
        public List<GameObject> obstacles = new List<GameObject>();

        [Header("障碍装饰（放在无地面格，高度在下表面与上表面之间）")]
        public List<GameObject> obstacleDecorations = new List<GameObject>();

        [Header("漂浮装饰（任意位置，不压障碍和建筑物，高度在地面之上）")]
        public List<GameObject> floatingDecorations = new List<GameObject>();

        [Header("鱼群（同漂浮装饰规则，最后生成）")]
        public List<GameObject> fishSchools = new List<GameObject>();

        [Tooltip("围栏素材长轴不是本地 X 轴时，用这个角度补正")]
        public int fenceYawOffset;

        [Tooltip("藤蔓素材贴面朝向不对时，用这个角度补正")]
        public int vineYawOffset;

        [Tooltip("同一区域挑中同一素材的偏移，换个数字可以整体换一套搭配")]
        public int variantSeed;

        // 同一区域固定取同一个 index，避免整张图看起来杂乱
        public GameObject PickGround(int group) => Pick(ground, group);

        public GameObject PickFence(int group) => Pick(fences, group);

        public GameObject PickShortPost(int group) => Pick(shortPosts, group);

        public GameObject PickTallPost(int group) => Pick(tallPosts, group);

        public GameObject PickSupport(int group) => Pick(supports, group);

        public GameObject PickVine(int group) => Pick(vines, group);

        public GameObject PickFrom(List<GameObject> list, int group) => Pick(list, group);

        // 同一区域固定用高/矮其中一种，避免一格一根高、一格一根矮
        public bool UseTallPost(int group) => ((group * 733 ^ variantSeed * 29) & 1) == 1;

        // 摆放数据只存"类别 + 列表下标"，运行时按这两个信息还原预制体
        public List<GameObject> GetList(TerrainItemKind kind)
        {
            switch (kind)
            {
                case TerrainItemKind.Ground: return ground;
                case TerrainItemKind.Fence: return fences;
                case TerrainItemKind.PostShort: return shortPosts;
                case TerrainItemKind.PostTall: return tallPosts;
                case TerrainItemKind.Support: return supports;
                case TerrainItemKind.Decoration: return decorations;
                case TerrainItemKind.Vine: return vines;
                case TerrainItemKind.Obstacle: return obstacles;
                case TerrainItemKind.ObstacleDecor: return obstacleDecorations;
                case TerrainItemKind.Floating: return floatingDecorations;
                case TerrainItemKind.Fish: return fishSchools;
                default: return null;
            }
        }

        public int IndexOf(TerrainItemKind kind, GameObject prefab)
        {
            List<GameObject> list = GetList(kind);
            return list == null ? -1 : list.IndexOf(prefab);
        }

        public GameObject GetPrefab(TerrainItemKind kind, int index)
        {
            List<GameObject> list = GetList(kind);
            if (list == null || index < 0 || index >= list.Count) return null;
            return list[index];
        }

        GameObject Pick(List<GameObject> list, int group)
        {
            if (list == null || list.Count == 0) return null;
            int mixed = group * 397 ^ variantSeed * 17;
            int index = Mathf.Abs(mixed) % list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                GameObject candidate = list[(index + i) % list.Count];
                if (candidate != null) return candidate;
            }
            return null;
        }
    }
}
