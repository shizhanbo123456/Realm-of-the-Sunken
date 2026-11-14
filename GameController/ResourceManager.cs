using System;
using System.Collections.Generic;
using TerrainGen;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

// 客户端资源管理器：大体积预制体走 Addressables（AssetReference）按需加载与延迟卸载。
// 角色/武器/怪物/场景物件在进游戏与进战斗时预加载后常驻，只有特效按实例回收：最后一个实例销毁后 3s 再卸载。
public class ResourceManager : MonoBehaviour
{
    private const float unloadDelay = 3f;
    private const int keyStride = 1000;

    private enum AssetState
    {
        Unloaded,
        Loading,
        Loaded,
        PendingUnload,   // 已无可用的持有者，等 3s 窗口到期
    }

    private class Entry
    {
        public AssetReferenceT<GameObject> reference;
        public AssetState state;
        public int preloadCount;                       // 常驻计数：归零才允许卸载
        public AsyncOperationHandle<GameObject> handle;
        public readonly List<GameObject> instances = new();
        public readonly List<Pending> waiting = new();   // 加载中排队的实例化请求
        public readonly List<Action> notify = new();     // 加载结束的完成通知（含失败）
        public Timer.TimerHandle unloadTimer;
    }

    // 调用方在请求时就已算好的落位：粒子在实例化的第一帧就会喷，不能等回调里再摆
    private struct Pending
    {
        public Vector3 position;
        public Quaternion rotation;
        public Action<GameObject> callback;
    }

    private void Awake()
    {
        InitReferenceMap();
        SkillIconMap = BuildIconMap(skillIconTextures);
        BuffIconMap = BuildIconMap(buffIconTextures);
        Tool.ResourceManager = this;
    }

    private void Start()
    {
        LoadNecessary();
    }

    #region 资源引用
    [SerializeField] private List<AssetReferenceT<GameObject>> AttackCharacterGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> DefenseCharacterGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> ZombieGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> EliteZombieGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> SageStatueGraphics = new();      // 副守护点 3 种，下标即 GuardStatue.value 0~2
    [SerializeField] private AssetReferenceT<GameObject> KingStatueGraphic;                    // 主守护点
    [SerializeField] private List<AssetReferenceT<GameObject>> CoralSmallGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> CoralLargeGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> CrystalStatueGraphics = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> NeutralStatueGraphics = new();

    [SerializeField] private List<AssetReferenceT<GameObject>> BulletVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> ShieldVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> RangeMagicVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> MagicCircleVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> BuffVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> HitVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> SlashVFX = new();
    [SerializeField] private List<AssetReferenceT<GameObject>> ProjectionVFX = new();

    [SerializeField] private List<AssetReferenceT<GameObject>> WeaponPrefabs = new();

    [SerializeField] private AssetReferenceT<GameObject> HomeDisplayArranger;   // 首页场景布置
    [SerializeField] private AssetReferenceT<GameObject> LobbyDisplayArranger;  // 大厅场景布置
    [SerializeField] private AssetReferenceT<BuildingKitSet> BuildingKit;       // 地形素材表（加载它即连带加载全部地形预制体）

    [SerializeField] private List<Texture> skillIconTextures = new();   // 贴图名前 3 位 = 技能 id
    [SerializeField] private List<Texture> buffIconTextures = new();    // 贴图名前 3 位 = Buff(EffectType) id
    public List<Texture2D> AttackCharacterIcons = new();
    public List<Texture2D> DefenseCharacterIcons = new();

    public Dictionary<int, Texture> SkillIconMap { get; private set; } = new();
    public Dictionary<int, Texture> BuffIconMap { get; private set; } = new();
    #endregion

    #region 预加载
    // 进游戏：攻守角色、武器、首页/大厅布置图、地形素材表。全部常驻，不参与卸载
    public void LoadNecessary()
    {
        PreloadAll(ResourceType.AttackCharacterGraphics);
        PreloadAll(ResourceType.DefenseCharacterGraphics);
        PreloadAll(ResourceType.WeaponPrefabs);
        Preload(ResourceType.HomeDisplayArranger, 0);
        Preload(ResourceType.LobbyDisplayArranger, 0);
        LoadBuildingKit(null);
    }

    // 进战斗：僵尸/精英僵尸、守护点、水晶与中立雕像、小/大珊瑚。加载完再放行开战
    public void LoadBattleNecessary(Action onDone)
    {
        PreloadGroup(onDone,
            ResourceType.ZombieGraphics,
            ResourceType.EliteZombieGraphics,
            ResourceType.SageStatueGraphics,
            ResourceType.KingStatueGraphic,
            ResourceType.CrystalStatueGraphics,
            ResourceType.NeutralStatueGraphics,
            ResourceType.CoralSmallGraphics,
            ResourceType.CoralLargeGraphics);
    }

    public void Preload(ResourceType type, int index)
    {
        int key = RefToInt(type, index);
        Entry entry = GetEntry(key);
        entry.preloadCount++;
        CancelUnload(entry);
        EnsureLoaded(key, entry);
    }

    public void PreloadAll(ResourceType type)
    {
        int count = CountOf(type);
        for (int index = 0; index < count; index++) Preload(type, index);
    }

    private void PreloadGroup(Action onDone, params ResourceType[] types)
    {
        int total = 0;
        for (int i = 0; i < types.Length; i++) total += CountOf(types[i]);
        if (total == 0)
        {
            onDone?.Invoke();
            return;
        }

        int finished = 0;
        Action step = () =>
        {
            finished++;
            if (finished >= total) onDone?.Invoke();
        };

        for (int i = 0; i < types.Length; i++)
        {
            int count = CountOf(types[i]);
            for (int index = 0; index < count; index++)
            {
                int key = RefToInt(types[i], index);
                Entry entry = GetEntry(key);
                entry.preloadCount++;
                CancelUnload(entry);
                if (entry.state == AssetState.Loaded)
                {
                    step();
                    continue;
                }
                entry.notify.Add(step);
                EnsureLoaded(key, entry);
            }
        }
    }
    #endregion

    #region 按需加载（特效）
    // 加载完成后回调一个已实例化的物体（落在调用方给的位姿上），持有者负责后续驱动与销毁
    public void Load(ResourceType type, int index, Vector3 position, Quaternion rotation, Action<GameObject> onLoaded)
    {
        if (onLoaded == null)
        {
            Debug.LogError($"[资源] Load 的回调为空：{type}[{index}]");
            return;
        }

        int key = RefToInt(type, index);
        Entry entry = GetEntry(key);
        CancelUnload(entry);
        if (entry.state == AssetState.Loaded)
        {
            onLoaded(Spawn(entry, position, rotation));
            return;
        }

        entry.waiting.Add(new Pending { position = position, rotation = rotation, callback = onLoaded });
        EnsureLoaded(key, entry);
    }
    #endregion

    #region 取模板（要求已加载）
    public bool TryGetGraphic(EntityType type, out GameObject prefab)
    {
        prefab = null;
        if (!GraphicIndexResolver.Resolve(type, CountOf, out ResourceType resourceType, out int index)) return false;
        if (index == GraphicIndexResolver.RandomIndex) index = GraphicIndexResolver.PickRandom(index, CountOf(resourceType));
        return TryGetPrefab(resourceType, index, out prefab);
    }

    // 武器是一维列表：编号即 entity.csv「武器编号」
    public bool TryGetWeaponPrefab(int weaponId, out GameObject prefab)
    {
        prefab = null;
        if (weaponId < 0 || weaponId >= CountOf(ResourceType.WeaponPrefabs)) return false;
        return TryGetPrefab(ResourceType.WeaponPrefabs, weaponId, out prefab);
    }

    public bool TryGetVfxPrefab(ResourceType type, int index, out GameObject prefab) => TryGetPrefab(type, index, out prefab);

    // 启动自检用：数量与引用是否配齐（不触发加载）
    public int AssetCount(ResourceType type) => CountOf(type);

    public bool IsAssetConfigured(ResourceType type, int index)
    {
        if (index < 0 || index >= CountOf(type)) return false;
        return entries.TryGetValue(RefToInt(type, index), out Entry entry)
            && entry.reference != null && entry.reference.RuntimeKeyIsValid();
    }

    public bool TryGetPrefab(ResourceType type, int index, out GameObject prefab)
    {
        prefab = null;
        int key = RefToInt(type, index);
        if (!entries.TryGetValue(key, out Entry entry)) return false;
        if (entry.state == AssetState.PendingUnload) CancelUnload(entry);   // 还在卸载窗口内：直接复用
        if (entry.state != AssetState.Loaded) return false;
        prefab = entry.handle.Result;
        return prefab != null;
    }
    #endregion

    #region 地形素材表
    private AsyncOperationHandle<BuildingKitSet> buildingKitHandle;
    private readonly List<Action> buildingKitWaiting = new();
    private bool buildingKitLoading;

    public bool IsBuildingKitLoaded => buildingKitHandle.IsValid() && buildingKitHandle.Status == AsyncOperationStatus.Succeeded;

    // 与常驻资源同样不卸载：地形全程都要用素材表
    public void LoadBuildingKit(Action<BuildingKitSet> onLoaded)
    {
        if (IsBuildingKitLoaded)
        {
            onLoaded?.Invoke(buildingKitHandle.Result);
            return;
        }
        if (onLoaded != null) buildingKitWaiting.Add(() => onLoaded(buildingKitHandle.Result));
        if (buildingKitLoading) return;

        if (BuildingKit == null || !BuildingKit.RuntimeKeyIsValid())
        {
            Debug.LogError("[资源] BuildingKit 未配置或 AssetReference 无效：地形无法生成");
            return;
        }

        buildingKitLoading = true;
        buildingKitHandle = BuildingKit.LoadAssetAsync();
        buildingKitHandle.Completed += OnBuildingKitLoaded;
    }

    private void OnBuildingKitLoaded(AsyncOperationHandle<BuildingKitSet> operation)
    {
        buildingKitLoading = false;
        if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
        {
            Debug.LogError($"[资源] 地形素材表加载失败：{operation.OperationException?.Message}");
            buildingKitWaiting.Clear();
            return;
        }

        var callbacks = new List<Action>(buildingKitWaiting);
        buildingKitWaiting.Clear();
        for (int i = 0; i < callbacks.Count; i++) callbacks[i]();
    }
    #endregion

    #region//Local
    private readonly Dictionary<int, Entry> entries = new();
    private readonly Dictionary<ResourceType, int> counts = new();
    private static readonly Predicate<GameObject> isDestroyed = instance => instance == null;

    private void InitReferenceMap()
    {
        AddList(ResourceType.AttackCharacterGraphics, AttackCharacterGraphics);
        AddList(ResourceType.DefenseCharacterGraphics, DefenseCharacterGraphics);
        AddList(ResourceType.ZombieGraphics, ZombieGraphics);
        AddList(ResourceType.EliteZombieGraphics, EliteZombieGraphics);
        AddList(ResourceType.SageStatueGraphics, SageStatueGraphics);
        AddSingle(ResourceType.KingStatueGraphic, KingStatueGraphic);
        AddList(ResourceType.CoralSmallGraphics, CoralSmallGraphics);
        AddList(ResourceType.CoralLargeGraphics, CoralLargeGraphics);
        AddList(ResourceType.CrystalStatueGraphics, CrystalStatueGraphics);
        AddList(ResourceType.NeutralStatueGraphics, NeutralStatueGraphics);
        AddList(ResourceType.BulletVFX, BulletVFX);
        AddList(ResourceType.ShieldVFX, ShieldVFX);
        AddList(ResourceType.RangeMagicVFX, RangeMagicVFX);
        AddList(ResourceType.MagicCircleVFX, MagicCircleVFX);
        AddList(ResourceType.BuffVFX, BuffVFX);
        AddList(ResourceType.HitVFX, HitVFX);
        AddList(ResourceType.SlashVFX, SlashVFX);
        AddList(ResourceType.ProjectionVFX, ProjectionVFX);
        AddList(ResourceType.WeaponPrefabs, WeaponPrefabs);
        AddSingle(ResourceType.HomeDisplayArranger, HomeDisplayArranger);
        AddSingle(ResourceType.LobbyDisplayArranger, LobbyDisplayArranger);
    }

    private void AddList(ResourceType type, List<AssetReferenceT<GameObject>> list)
    {
        counts[type] = list.Count;
        for (int index = 0; index < list.Count; index++)
            entries.Add(RefToInt(type, index), new Entry { reference = list[index] });
    }

    private void AddSingle(ResourceType type, AssetReferenceT<GameObject> reference)
    {
        counts[type] = reference != null && reference.RuntimeKeyIsValid() ? 1 : 0;
        entries.Add(RefToInt(type, 0), new Entry { reference = reference });
    }

    private int CountOf(ResourceType type) => counts.TryGetValue(type, out int count) ? count : 0;

    private void EnsureLoaded(int key, Entry entry)
    {
        if (entry.state == AssetState.PendingUnload) CancelUnload(entry);
        if (entry.state == AssetState.Loading || entry.state == AssetState.Loaded) return;

        if (entry.reference == null || !entry.reference.RuntimeKeyIsValid())
        {
            Debug.LogError($"[资源] {Describe(key)} 未配置或 AssetReference 无效");
            entry.state = AssetState.Unloaded;
            entry.waiting.Clear();
            FlushNotify(entry);
            return;
        }

        entry.state = AssetState.Loading;
        entry.handle = entry.reference.LoadAssetAsync();
        entry.handle.Completed += operation => OnLoadCompleted(key, entry, operation);
    }

    private void OnLoadCompleted(int key, Entry entry, AsyncOperationHandle<GameObject> operation)
    {
        if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
        {
            Debug.LogError($"[资源] 加载失败：{Describe(key)}：{operation.OperationException?.Message}");
            entry.state = AssetState.Unloaded;
            entry.handle = default;
            entry.waiting.Clear();
            FlushNotify(entry);
            return;
        }

        entry.state = AssetState.Loaded;

        if (entry.waiting.Count > 0)
        {
            var callbacks = new List<Pending>(entry.waiting);
            entry.waiting.Clear();
            for (int i = 0; i < callbacks.Count; i++)
            {
                Pending pending = callbacks[i];
                pending.callback(Spawn(entry, pending.position, pending.rotation));
            }
        }
        FlushNotify(entry);
    }

    // 加载失败也要通知，否则依赖完成通知的加载阶段会一直卡住
    private void FlushNotify(Entry entry)
    {
        if (entry.notify.Count == 0) return;
        var callbacks = new List<Action>(entry.notify);
        entry.notify.Clear();
        for (int i = 0; i < callbacks.Count; i++) callbacks[i]();
    }

    private GameObject Spawn(Entry entry, Vector3 position, Quaternion rotation)
    {
        var instance = Instantiate(entry.handle.Result, position, rotation);
        entry.instances.Add(instance);
        return instance;
    }

    private void Update()
    {
        foreach (var pair in entries)
        {
            Entry entry = pair.Value;
            if (entry.instances.Count == 0) continue;

            entry.instances.RemoveAll(isDestroyed);
            if (entry.instances.Count > 0 || entry.preloadCount > 0) continue;
            if (entry.state != AssetState.Loaded) continue;
            StartUnloadTimer(pair.Key, entry);
        }
    }

    private void StartUnloadTimer(int key, Entry entry)
    {
        entry.state = AssetState.PendingUnload;
        entry.unloadTimer = Timer.AddTimer(key, FinishUnload, unloadDelay, 1, false);
    }

    // 3s 窗口内又有请求会 Cancel 掉计时器（见 CancelUnload），走到这里说明确实没人用了
    private void FinishUnload(int key)
    {
        Entry entry = entries[key];
        entry.unloadTimer = null;
        if (entry.preloadCount > 0 || entry.instances.Count > 0)
        {
            entry.state = AssetState.Loaded;
            return;
        }

        entry.state = AssetState.Unloaded;
        if (entry.handle.IsValid()) Addressables.Release(entry.handle);
        entry.handle = default;
    }

    private void CancelUnload(Entry entry)
    {
        if (entry.unloadTimer == null) return;
        entry.unloadTimer.Cancel();
        entry.unloadTimer = null;
        entry.state = AssetState.Loaded;
    }

    private Entry GetEntry(int key)
    {
        if (entries.TryGetValue(key, out Entry entry)) return entry;
        throw new KeyNotFoundException($"[资源] key {key} 没有对应资源（{Describe(key)}），请核对 ResourceType 与下标");
    }

    private static int RefToInt(ResourceType type, int index)
    {
        if (index < 0 || index >= keyStride)
            throw new ArgumentOutOfRangeException(nameof(index), $"[资源] {type} 的下标 {index} 超出 0~{keyStride - 1}：key 编码会串到相邻类别");
        return (int)type * keyStride + index;
    }

    private static void IntToRef(int value, out ResourceType type, out int index)
    {
        type = (ResourceType)(value / keyStride);
        index = value % keyStride;
    }

    private static string Describe(int key)
    {
        IntToRef(key, out ResourceType type, out int index);
        return $"{type}[{index}]";
    }

    // 图标 id 取贴图名前 3 位（如 "015_霜棱散" → 15）；名字不合规或 id 重复属资源配置错配，直接抛
    private static Dictionary<int, Texture> BuildIconMap(List<Texture> textures)
    {
        var map = new Dictionary<int, Texture>();
        if (textures == null)
            throw new FormatException("[图标] ResourceManager 的图标列表为空引用，请在 Inspector 里重新配置");

        foreach (var texture in textures)
        {
            if (texture == null)
                throw new FormatException("[图标] ResourceManager 的图标列表含空引用，请检查 Inspector 列表");
            string textureName = texture.name;
            if (textureName.Length < 3 || !int.TryParse(textureName.Substring(0, 3), out int id))
                throw new FormatException($"[图标] ResourceManager 的贴图「{textureName}」前 3 位不是图标 id");
            if (map.ContainsKey(id))
                throw new FormatException($"[图标] ResourceManager 的贴图「{textureName}」id {id} 与「{map[id].name}」重复");
            map.Add(id, texture);
        }
        return map;
    }
    #endregion
}

public enum ResourceType
{
    AttackCharacterGraphics,
    DefenseCharacterGraphics,
    ZombieGraphics,
    EliteZombieGraphics,
    SageStatueGraphics,
    KingStatueGraphic,
    CoralSmallGraphics,
    CoralLargeGraphics,
    CrystalStatueGraphics,
    NeutralStatueGraphics,
    BulletVFX,
    ShieldVFX,
    RangeMagicVFX,
    MagicCircleVFX,
    BuffVFX,
    HitVFX,
    SlashVFX,
    ProjectionVFX,
    WeaponPrefabs,
    HomeDisplayArranger,
    LobbyDisplayArranger,
}
