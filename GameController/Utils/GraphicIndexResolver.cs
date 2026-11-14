using System;
using UnityEngine;

// EntityType → 图形类别与下标。两端共用同一套选取规则：角色按下标、僵尸/水晶/中立按变体取模、
// 珊瑚整类随机、守护点按副/主分流。取模与随机都依赖列表长度，故由调用方提供 CountOf。
public static class GraphicIndexResolver
{
    public const int RandomIndex = -1;   // 该类别内随机取一个（珊瑚）

    public static bool Resolve(EntityType type, Func<ResourceType, int> countOf,
        out ResourceType resourceType, out int index)
    {
        switch (type.category)
        {
            case EntityCategory.Character_Attack:
                return Direct(ResourceType.AttackCharacterGraphics, type.value, countOf, out resourceType, out index);
            case EntityCategory.Character_Defense:
                return Direct(ResourceType.DefenseCharacterGraphics, type.value, countOf, out resourceType, out index);
            case EntityCategory.Zombie:
                return Wrap(ResourceType.ZombieGraphics, type.value, countOf, out resourceType, out index);
            case EntityCategory.EliteZombie:
                return Wrap(ResourceType.EliteZombieGraphics, type.value, countOf, out resourceType, out index);
            case EntityCategory.GuardStatue:
                // 副守护点 value 0~2 各用自己那一种；主守护点 value >= sage_statue_count 用国王
                if (type.value >= GlobalConfig.sage_statue_count)
                    return Direct(ResourceType.KingStatueGraphic, 0, countOf, out resourceType, out index);
                return Direct(ResourceType.SageStatueGraphics, type.value, countOf, out resourceType, out index);
            case EntityCategory.Coral:
                // 小/大珊瑚各用自己列表，列表内随机取一个变体（每个实体创建时只取一次）
                resourceType = type.value <= 0 ? ResourceType.CoralSmallGraphics : ResourceType.CoralLargeGraphics;
                index = RandomIndex;
                return countOf(resourceType) > 0;
            case EntityCategory.CrystalStatue:
                return Wrap(ResourceType.CrystalStatueGraphics, type.value, countOf, out resourceType, out index);
            case EntityCategory.NeutralStatue:
                return Wrap(ResourceType.NeutralStatueGraphics, type.value, countOf, out resourceType, out index);
            default:
                resourceType = default;
                index = -1;
                return false;
        }
    }

    // 下标直取：角色与副守护点，越界即视为配置缺失
    private static bool Direct(ResourceType type, int value, Func<ResourceType, int> countOf,
        out ResourceType resourceType, out int index)
    {
        resourceType = type;
        index = value;
        return value >= 0 && value < countOf(type);
    }

    // 按下标取模：变体数少于实例编号时循环复用
    private static bool Wrap(ResourceType type, int value, Func<ResourceType, int> countOf,
        out ResourceType resourceType, out int index)
    {
        resourceType = type;
        int count = countOf(type);
        index = count > 0 ? value % count : -1;
        return count > 0;
    }

    // 随机取一个：把 RandomIndex 换成具体下标
    public static int PickRandom(int index, int count)
    {
        return index == RandomIndex ? UnityEngine.Random.Range(0, count) : index;
    }
}
