using System.Collections.Generic;
using System.Threading;
using UnityEngine;

using Ros.Config;
[ExecuteInEditMode]
public class Tool : MonoBehaviour
{
    [Range(0,100)] public int delay = 20;

    [Header("启动自检")]
    public bool isServer;

    private void Awake()
    {
        Instance = this;
    }

    private int _frameCount;
    private float _frameTimer;

    private void Update()
    {
        Thread.Sleep(delay);
        Timer.Update();

        _frameCount++;
        _frameTimer += Time.unscaledDeltaTime;
        if (_frameTimer >= 1f)
        {
            Debug.Log($"[FPS] 每秒经过帧数: {_frameCount}");
            _frameCount = 0;
            _frameTimer -= 1f;
        }
    }

    private void Start()
    {
        if (!Application.isPlaying) return; // 本类带 ExecuteInEditMode，编辑模式也会跑 Start
        Timer.AddTimer(this, _ => RunStartupCheck(), startupCheckDelay, 1, false);
    }

    public static Tool Instance { get; private set; }

    // 服务器/客户端的唯一判定口径（Inspector 上的 isServer）
    public static bool IsServer => Instance != null && Instance.isServer;

    #region 管理器引用（由各管理器 Awake 注册）
    public static Ros.Config.ConfigManager ConfigManager;
    public static NetworkManager NetworkManager;
    public static BattleManager BattleManager;
    public static InfoManager InfoManager;
    public static AssetsManager AssetsManager;
    public static InputManager InputManager;
    public static SaveManager SaveManager;
    public static EnvironmentManager EnvironmentManager;
    public static CameraController CameraController;
    public static ClientLogicManager ClientLogicManager;
    public static UIManager UIManager;
    public static VfxManager VfxManager;
    public static TransitionManager TransitionManager;
    public static LuaManager LuaManager;
    public static TerrainGen.ModularTerrainGenerator ModularTerrainGenerator;
    public static ResourceManager ResourceManager;
    #endregion

    #region 通用工具
    private static Transform t_calTransform;
    public static Transform GetTemporaryTransform()
    {
        if (t_calTransform == null)
        {
            t_calTransform = new GameObject("calTransform").transform;
            DontDestroyOnLoad(t_calTransform.gameObject);
        }
        return t_calTransform;
    }
    public static void ActiveFor<T>(List<T> list, int count) where T : Component
    {
        while (list.Count < count)
        {
            list.Add(Instantiate(list[0].gameObject, list[0].transform.parent).GetComponent<T>());
        }
        for (int i = 0; i < list.Count; i++)
        {
            list[i].gameObject.SetActive(i < count);
        }
    }
    public static void ActiveFor(List<GameObject> list, int count)
    {
        while (list.Count < count)
        {
            list.Add(Instantiate(list[0], list[0].transform.parent));
        }
        for (int i = 0; i < list.Count; i++)
        {
            list[i].gameObject.SetActive(i < count);
        }
    }
    public static void ExpandListTo<T>(List<T> list, int count, T value = default)
    {
        while (list.Count < count)
        {
            list.Add(value);
        }
    }
    #endregion

    #region 启动自检
    private void RunStartupCheck()
    {
        int errors = 0;

        errors += CheckObject("Tool 自身", Instance);
        errors += CheckObject(nameof(NetworkManager), NetworkManager);
        errors += CheckObject(nameof(BattleManager), BattleManager);
        errors += CheckObject(nameof(InfoManager), InfoManager);
        if (!isServer)
        {
            // 昼夜表现只在客户端，服务器没有 EnvironmentManager
            errors += CheckObject(nameof(EnvironmentManager), EnvironmentManager);
            errors += CheckObject(nameof(InputManager), InputManager);
            errors += CheckObject(nameof(SaveManager), SaveManager);
            errors += CheckObject(nameof(CameraController), CameraController);
            errors += CheckObject(nameof(ClientLogicManager), ClientLogicManager);
            errors += CheckObject(nameof(VfxManager), VfxManager);
            errors += CheckObject(nameof(LuaManager), LuaManager);
        }

        errors += CheckInfoManager();
        errors += CheckCharacterRows(EntityCategory.Character_Attack, GlobalConfig.attack_character_count, "进攻方");
        errors += CheckCharacterRows(EntityCategory.Character_Defense, GlobalConfig.defense_character_count, "防守方");
        errors += CheckGuardStatueSpawns();

        // 资源来源按端分：服务器同步读 AssetsManager 的直接引用，客户端读 ResourceManager 的 AssetReference
        if (isServer)
        {
            errors += CheckObject(nameof(AssetsManager), AssetsManager);
            errors += CheckEntityGraphics();
            errors += CheckServerVfxAssets();
        }
        else
        {
            errors += CheckObject(nameof(ResourceManager), ResourceManager);
            errors += CheckClientAssets();
        }

        string mode = isServer ? "服务器" : "客户端";
        if (errors == 0) Debug.Log($"[启动自检] 通过（{mode}）");
        else Debug.LogError($"[启动自检] {mode}：共 {errors} 处问题，详见上方日志");
    }

    private static int CheckInfoManager()
    {
        var m = InfoManager;
        if (m == null) return 0; // 缺失已在管理器检查里报过

        int e = 0;
        if (m.entity_layer == 0)
        {
            Debug.LogError("[启动自检] InfoManager.entity_layer 未配置（当前 0）：物理查询会与 Default 层混在一起");
            e++;
        }
        if (m.ground_layer == 0)
        {
            Debug.LogError("[启动自检] InfoManager.ground_layer 未配置（当前 0）：落地检测会退化为 Default 层");
            e++;
        }
        if (m.entity_layer != 0 && m.entity_layer == m.ground_layer)
        {
            Debug.LogError($"[启动自检] InfoManager 的 entity_layer 与 ground_layer 相同（{LayerName(m.entity_layer)}）：落地检测会把实体自身当地面，命中查询也会混层");
            e++;
        }

        return e;
    }

    private static int CheckGuardStatueSpawns()
    {
        if (!TerrainGen.TerrainSpawns.Built)
        {
            Debug.LogWarning("[启动自检] 地形尚未生成（TerrainSpawns 未构建）：跳过守护点自检");
            return 0;
        }

        int e = 0;
        int subCount = TerrainGen.TerrainSpawns.SubGuards.Count;
        if (subCount != GlobalConfig.sage_statue_count)
        {
            Debug.LogError($"[启动自检] 地形烘焙数据里有 {subCount} 个副守护点，与贤者雕像数量 {GlobalConfig.sage_statue_count} 不一致：雕像会多出或缺失");
            e++;
        }
        return e;
    }

    // entity.csv 必须与 Config 的角色数量对齐：缺行运行时 Get 直接抛，多余行不参与但会残留
    private static int CheckCharacterRows(EntityCategory category, int count, string label)
    {
        int e = 0;
        for (int i = 0; i < count; i++)
        {
            if (EntityConfig.content.ContainsKey(new EntityType(category, i))) continue;
            Debug.LogError($"[启动自检] entity.csv 缺少{label}角色行 (value={i})：选到该角色会直接抛异常");
            e++;
        }

        int rows = 0;
        foreach (var key in EntityConfig.content.Keys)
            if (key.category == category) rows++;
        if (rows > count) Debug.LogWarning($"[启动自检] entity.csv 的{label}角色有 {rows} 行，多于角色数量 {count}：多出的行不会被选中，建议删除");
        return e;
    }

    private static int CheckEntityGraphics()
    {
        var m = AssetsManager;
        if (m == null) return 0;

        int e = 0;
        e += CheckList("AssetsManager.AttackCharacterGraphics", m.AttackCharacterGraphics, GlobalConfig.attack_character_count);
        e += CheckList("AssetsManager.DefenseCharacterGraphics", m.DefenseCharacterGraphics, GlobalConfig.defense_character_count);
        e += CheckList("AssetsManager.ZombieGraphics", m.ZombieGraphics, GlobalConfig.zombie_variant_count);
        e += CheckList("AssetsManager.EliteZombieGraphics", m.EliteZombieGraphics, GlobalConfig.elite_zombie_variant_count);
        e += CheckList("AssetsManager.SageStatueGraphics", m.SageStatueGraphics, GlobalConfig.sage_statue_count);
        e += CheckObject("AssetsManager.KingStatueGraphic", m.KingStatueGraphic);
        e += CheckList("AssetsManager.CoralSmallGraphics", m.CoralSmallGraphics, 0);
        e += CheckList("AssetsManager.CoralLargeGraphics", m.CoralLargeGraphics, 0);
        e += CheckList("AssetsManager.CrystalStatueGraphics", m.CrystalStatueGraphics, GlobalConfig.crystal_statue_count);
        e += CheckList("AssetsManager.NeutralStatueGraphics", m.NeutralStatueGraphics, GlobalConfig.neutral_statue_variant_count);

        // 数量/空项之外再逐项核对：组件没挂、范围没录、碰撞体层不对，运行时照样打不中或不动
        e += CheckPrefabs("AssetsManager.AttackCharacterGraphics", m.AttackCharacterGraphics, humanoid: true);
        e += CheckPrefabs("AssetsManager.DefenseCharacterGraphics", m.DefenseCharacterGraphics, humanoid: true);
        e += CheckPrefabs("AssetsManager.ZombieGraphics", m.ZombieGraphics, humanoid: true);
        e += CheckPrefabs("AssetsManager.EliteZombieGraphics", m.EliteZombieGraphics, humanoid: true);
        e += CheckPrefabs("AssetsManager.SageStatueGraphics", m.SageStatueGraphics, humanoid: false);
        e += CheckPrefab("AssetsManager.KingStatueGraphic", m.KingStatueGraphic, humanoid: false);
        e += CheckPrefabs("AssetsManager.CoralSmallGraphics", m.CoralSmallGraphics, humanoid: false);
        e += CheckPrefabs("AssetsManager.CoralLargeGraphics", m.CoralLargeGraphics, humanoid: false);
        e += CheckPrefabs("AssetsManager.CrystalStatueGraphics", m.CrystalStatueGraphics, humanoid: false);
        e += CheckPrefabs("AssetsManager.NeutralStatueGraphics", m.NeutralStatueGraphics, humanoid: false);
        return e;
    }

    private static int CheckPrefabs(string label, List<GameObject> list, bool humanoid)
    {
        if (list == null) return 0;

        int e = 0;
        for (int i = 0; i < list.Count; i++)
            if (list[i] != null) e += CheckEntityPrefab($"{label}[{i}]", list[i], humanoid);
        return e;
    }

    private static int CheckPrefab(string label, GameObject prefab, bool humanoid)
    {
        return prefab == null ? 0 : CheckEntityPrefab(label, prefab, humanoid);
    }

    // 单个实体图形：EntityModelInfo 是否挂上、包围范围是否有效、胶囊碰撞体会落在哪一层、人形是否挂 EntityAnim（非人形不应挂）
    private static int CheckEntityPrefab(string label, GameObject prefab, bool humanoid)
    {
        int e = 0;
        string name = $"{label}「{prefab.name}」";

        var modelInfo = prefab.GetComponentInChildren<EntityModelInfo>(true);
        if (modelInfo == null)
        {
            Debug.LogError($"[启动自检] {name} 没挂 EntityModelInfo：运行时补不出胶囊碰撞体与头顶锚点，实体无法被命中");
            return 1;
        }

        // 半径是固定值，只校验高度：yRange 没录入时高度为 0，碰撞体失效
        float height = modelInfo.yRange.y - modelInfo.yRange.x;
        if (height <= 0f)
        {
            Debug.LogError($"[启动自检] {name} 的 EntityModelInfo 高度范围未录入（yRange={modelInfo.yRange}）：胶囊碰撞体高度为 0，实体很可能打不中");
            e++;
        }

        // 运行时补的 CapsuleCollider 挂在 EntityModelInfo 所在物体上，物体在哪层碰撞体就在哪层
        var host = modelInfo.gameObject;
        int entityLayer = InfoManager != null ? InfoManager.entity_layer : 0;
        if (entityLayer > 0 && host.layer != entityLayer)
        {
            Debug.LogError($"[启动自检] {name} 的 EntityModelInfo 在「{host.name}」（{LayerName(host.layer)} 层），应为实体层 {LayerName(entityLayer)}：物理命中查询会漏掉这个实体");
            e++;
        }

        var capsule = host.GetComponent<CapsuleCollider>();
        if (capsule != null && (!capsule.enabled || capsule.isTrigger))
        {
            Debug.LogError($"[启动自检] {name} 已有 CapsuleCollider 但被禁用或是 Trigger：命中判定会跳过它");
            e++;
        }

        // 只有人形（角色/僵尸）挂 EntityAnim：它决定是否建刚体与驱动动画，雕像与珊瑚不该有
        var anim = prefab.GetComponentInChildren<EntityAnim>(true);
        if (humanoid && anim == null)
        {
            Debug.LogError($"[启动自检] {name} 是人形预制体但没挂 EntityAnim：运行时不会建刚体，实体不会移动也没有受击动作");
            e++;
        }
        else if (!humanoid && anim != null)
        {
            Debug.LogError($"[启动自检] {name} 是非人形预制体（雕像/珊瑚）却挂了 EntityAnim：会被当人形建刚体并驱动动画");
            e++;
        }
        return e;
    }

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return name.Length > 0 ? $"{name}({layer})" : layer.ToString();
    }

    // 服务器：特效与武器是判定侧资产（命中球、存活时长），数量与命中球一并校验
    private static int CheckServerVfxAssets()
    {
        var m = AssetsManager;
        if (m == null) return 0;

        int e = 0;
        e += CheckList("AssetsManager.BulletVFX", m.BulletVFX, vfx_bullet_count);
        e += CheckList("AssetsManager.ShieldVFX", m.ShieldVFX, vfx_shield_count);
        e += CheckList("AssetsManager.RangeMagicVFX", m.RangeMagicVFX, vfx_range_magic_count);
        e += CheckList("AssetsManager.MagicCircleVFX", m.MagicCircleVFX, vfx_magic_circle_count);
        e += CheckList("AssetsManager.BuffVFX", m.BuffVFX, vfx_buff_count);
        e += CheckList("AssetsManager.HitVFX", m.HitVFX, vfx_hit_count);
        e += CheckList("AssetsManager.SlashVFX", m.SlashVFX, vfx_slash_count);
        e += CheckList("AssetsManager.ProjectionVFX", m.ProjectionVFX, vfx_projection_count);
        e += CheckList("AssetsManager.WeaponPrefabs", m.WeaponPrefabs, weapon_count);

        // Buff 特效按 hits[0] 贴合实体高度，缺命中球就没法定位/缩放（映射见 effect.csv 第 11、12 列）
        foreach (var row in EffectConfig.content.Values)
        {
            if (!row.vfxKind.HasValue) continue;
            if (VfxHitSource.GetHits((Ros.Skill.SkillVfxKind)row.vfxKind.Value, row.vfxIndex.Value).Length > 0) continue;
            Debug.LogError($"[启动自检] Buff {row.key}「{row.name}」→ {row.vfxKind.Value}[{row.vfxIndex.Value}] 没有 BulletVfxInfo.hits：无法贴合实体高度");
            e++;
        }
        return e;
    }

    // 客户端：只校验 AssetReference 是否配齐、图标是否到位；预制体本体要到加载后才可验
    private static int CheckClientAssets()
    {
        var m = ResourceManager;
        if (m == null) return 0;

        int e = 0;
        e += CheckReferences("ResourceManager.AttackCharacterGraphics", m, ResourceType.AttackCharacterGraphics, GlobalConfig.attack_character_count);
        e += CheckReferences("ResourceManager.DefenseCharacterGraphics", m, ResourceType.DefenseCharacterGraphics, GlobalConfig.defense_character_count);
        e += CheckReferences("ResourceManager.ZombieGraphics", m, ResourceType.ZombieGraphics, GlobalConfig.zombie_variant_count);
        e += CheckReferences("ResourceManager.EliteZombieGraphics", m, ResourceType.EliteZombieGraphics, GlobalConfig.elite_zombie_variant_count);
        e += CheckReferences("ResourceManager.SageStatueGraphics", m, ResourceType.SageStatueGraphics, GlobalConfig.sage_statue_count);
        e += CheckReferences("ResourceManager.CrystalStatueGraphics", m, ResourceType.CrystalStatueGraphics, GlobalConfig.crystal_statue_count);
        e += CheckReferences("ResourceManager.NeutralStatueGraphics", m, ResourceType.NeutralStatueGraphics, GlobalConfig.neutral_statue_variant_count);
        e += CheckReferences("ResourceManager.KingStatueGraphic", m, ResourceType.KingStatueGraphic, 1);
        e += CheckReferences("ResourceManager.HomeDisplayArranger", m, ResourceType.HomeDisplayArranger, 1);
        e += CheckReferences("ResourceManager.LobbyDisplayArranger", m, ResourceType.LobbyDisplayArranger, 1);
        e += CheckReferences("ResourceManager.CoralSmallGraphics", m, ResourceType.CoralSmallGraphics, 0);
        e += CheckReferences("ResourceManager.CoralLargeGraphics", m, ResourceType.CoralLargeGraphics, 0);
        e += CheckReferences("ResourceManager.BulletVFX", m, ResourceType.BulletVFX, vfx_bullet_count);
        e += CheckReferences("ResourceManager.ShieldVFX", m, ResourceType.ShieldVFX, vfx_shield_count);
        e += CheckReferences("ResourceManager.RangeMagicVFX", m, ResourceType.RangeMagicVFX, vfx_range_magic_count);
        e += CheckReferences("ResourceManager.MagicCircleVFX", m, ResourceType.MagicCircleVFX, vfx_magic_circle_count);
        e += CheckReferences("ResourceManager.BuffVFX", m, ResourceType.BuffVFX, vfx_buff_count);
        e += CheckReferences("ResourceManager.HitVFX", m, ResourceType.HitVFX, vfx_hit_count);
        e += CheckReferences("ResourceManager.SlashVFX", m, ResourceType.SlashVFX, vfx_slash_count);
        e += CheckReferences("ResourceManager.ProjectionVFX", m, ResourceType.ProjectionVFX, vfx_projection_count);
        e += CheckReferences("ResourceManager.WeaponPrefabs", m, ResourceType.WeaponPrefabs, weapon_count);

        e += CheckList("ResourceManager.AttackCharacterIcons", m.AttackCharacterIcons, GlobalConfig.attack_character_count);
        e += CheckList("ResourceManager.DefenseCharacterIcons", m.DefenseCharacterIcons, GlobalConfig.defense_character_count);
        return e;
    }

    private static int CheckReferences(string label, ResourceManager m, ResourceType type, int expected)
    {
        int count = m.AssetCount(type);
        int e = 0;
        if (expected > 0 && count != expected)
        {
            if (count < expected) { Debug.LogError($"[启动自检] {label} 数量不足：{count} / 期望 {expected}"); e++; }
            else Debug.LogWarning($"[启动自检] {label} 数量偏多：{count} / 期望 {expected}");
        }
        for (int i = 0; i < count; i++)
        {
            if (m.IsAssetConfigured(type, i)) continue;
            Debug.LogError($"[启动自检] {label}[{i}] 的 AssetReference 为空或未指向 addressable 资产");
            e++;
        }
        return e;
    }
    #endregion

    #region//Local
    private const float startupCheckDelay = 1f;

    // 以下数量在 Config 里没有对应常量，来源见注释
    private const int vfx_bullet_count = 60;      // 《特效清单与分配表》第二节：20 类 × 3 颜色
    private const int vfx_shield_count = 13;
    private const int vfx_range_magic_count = 10;
    private const int vfx_magic_circle_count = 10;
    private const int vfx_buff_count = 31;
    private const int vfx_hit_count = 2;          // 下标 0 = buff/DoT 类（实体中心），1 = 直接伤害（命中点高度 + 实体所在列）
    private const int vfx_slash_count = 19;       // Assets/Files/Prefabs/VFX/Slashes
    private const int vfx_projection_count = 50;  // Assets/Files/Prefabs/VFX/Projection：10 种样式 × 5 颜色
    private const int weapon_count = 14;          // 武器预制体一维列表长度

    private static int CheckObject(string label, UnityEngine.Object obj)
    {
        if (obj == null)
        {
            Debug.LogError($"[启动自检] {label} 为空（缺失或未配置）");
            return 1;
        }
        return 0;
    }

    private static int CheckList<T>(string label, List<T> list, int expected) where T : UnityEngine.Object
    {
        if (list == null)
        {
            Debug.LogError($"[启动自检] {label} 为 null（未初始化）");
            return 1;
        }

        int e = 0;
        if (expected > 0 && list.Count != expected)
        {
            if (list.Count < expected) { Debug.LogError($"[启动自检] {label} 数量不足：{list.Count} / 期望 {expected}"); e++; }
            else Debug.LogWarning($"[启动自检] {label} 数量偏多：{list.Count} / 期望 {expected}");
        }
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == null)
            {
                Debug.LogError($"[启动自检] {label}[{i}] 为空（资产可能已被删除）");
                e++;
            }
        }
        return e;
    }
    #endregion
}

