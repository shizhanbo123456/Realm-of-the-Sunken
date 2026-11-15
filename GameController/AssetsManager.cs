using System.Collections.Generic;
using UnityEngine;

// 服务器资产引用管理器：服务器全程同步走直接引用。实体生成、命中球判定与武器显示共用同一份预制体，
// 不经过 Addressables。客户端场景不挂本组件，改由 ResourceManager 提供资源。
public class AssetsManager : MonoBehaviour
{
    private void Awake()
    {
        Tool.AssetsManager = this;
    }

    #region 实体图形
    public List<GameObject> AttackCharacterGraphics = new();
    public List<GameObject> DefenseCharacterGraphics = new();
    public List<GameObject> ZombieGraphics = new();
    public List<GameObject> EliteZombieGraphics = new();
    public List<GameObject> SageStatueGraphics = new();      // 副守护点 3 种，下标即 GuardStatue.value 0~2
    public GameObject KingStatueGraphic;                    // 主守护点
    public List<GameObject> CoralSmallGraphics = new();
    public List<GameObject> CoralLargeGraphics = new();
    public List<GameObject> CrystalStatueGraphics = new();
    public List<GameObject> NeutralStatueGraphics = new();
    #endregion

    #region 特效（编号与《特效清单与分配表.md》对应）
    public List<GameObject> BulletVFX = new();
    public List<GameObject> ShieldVFX = new();
    public List<GameObject> RangeMagicVFX = new();
    public List<GameObject> MagicCircleVFX = new();
    public List<GameObject> BuffVFX = new();
    public List<GameObject> HitVFX = new();
    public List<GameObject> SlashVFX = new();
    public List<GameObject> ProjectionVFX = new();
    #endregion

    #region 武器（悬浮武器模型，SelectedWeaponPrefabCreator 生成到 Assets/Files/Prefabs/Weapons）
    public List<GameObject> WeaponPrefabs = new();
    #endregion

    #region 地形素材（服务器直接引用；客户端不挂本组件，改由 ResourceManager 的 BuildingKit 提供）
    public TerrainGen.BuildingKitSet TerrainKit;
    #endregion

    public bool TryGetGraphic(EntityType type, out GameObject graphic)
    {
        graphic = null;
        if (!GraphicIndexResolver.Resolve(type, CountOf, out ResourceType resourceType, out int index)) return false;
        graphic = At(resourceType, index);
        return graphic != null;
    }

    // 武器是一维列表：编号即 entity.csv「武器编号」
    public bool TryGetWeaponPrefab(int weaponId, out GameObject prefab)
    {
        prefab = null;
        if (weaponId < 0 || weaponId >= WeaponPrefabs.Count) return false;
        prefab = WeaponPrefabs[weaponId];
        return prefab != null;
    }

    private int CountOf(ResourceType type)
    {
        if (type == ResourceType.KingStatueGraphic) return KingStatueGraphic != null ? 1 : 0;
        List<GameObject> list = ListOf(type);
        return list != null ? list.Count : 0;
    }

    private GameObject At(ResourceType type, int index)
    {
        if (type == ResourceType.KingStatueGraphic) return index == 0 ? KingStatueGraphic : null;

        List<GameObject> list = ListOf(type);
        if (list == null || list.Count == 0) return null;
        if (index == GraphicIndexResolver.RandomIndex) index = GraphicIndexResolver.PickRandom(index, list.Count);
        return index >= 0 && index < list.Count ? list[index] : null;
    }

    private List<GameObject> ListOf(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.AttackCharacterGraphics: return AttackCharacterGraphics;
            case ResourceType.DefenseCharacterGraphics: return DefenseCharacterGraphics;
            case ResourceType.ZombieGraphics: return ZombieGraphics;
            case ResourceType.EliteZombieGraphics: return EliteZombieGraphics;
            case ResourceType.SageStatueGraphics: return SageStatueGraphics;
            case ResourceType.CoralSmallGraphics: return CoralSmallGraphics;
            case ResourceType.CoralLargeGraphics: return CoralLargeGraphics;
            case ResourceType.CrystalStatueGraphics: return CrystalStatueGraphics;
            case ResourceType.NeutralStatueGraphics: return NeutralStatueGraphics;
            case ResourceType.BulletVFX: return BulletVFX;
            case ResourceType.ShieldVFX: return ShieldVFX;
            case ResourceType.RangeMagicVFX: return RangeMagicVFX;
            case ResourceType.MagicCircleVFX: return MagicCircleVFX;
            case ResourceType.BuffVFX: return BuffVFX;
            case ResourceType.HitVFX: return HitVFX;
            case ResourceType.SlashVFX: return SlashVFX;
            case ResourceType.ProjectionVFX: return ProjectionVFX;
            case ResourceType.WeaponPrefabs: return WeaponPrefabs;
            default: return null;
        }
    }
}
