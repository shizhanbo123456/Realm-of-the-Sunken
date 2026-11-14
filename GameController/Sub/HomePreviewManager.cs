using System.Collections.Generic;
using UnityEngine;

using Ros.Config;
// 首页/大厅的场景展示：复制对应的 Arranger 预制体（含场景与锚点），摆所选角色并摆相机
public class HomePreviewManager : ClientSubManager
{
    private struct ViewEntry
    {
        public EntityType type;
        public GameObject go;
    }

    private readonly List<ViewEntry> views = new();                 // 场上现有视图
    private readonly List<(EntityType type, Transform anchor)> slots = new(); // 本次期望摆位
    private readonly Dictionary<EntityType, List<GameObject>> pool = new();   // 复用池：类型 → 可用视图
    private HomeDisplayArranger homeStage;
    private LobbyDisplayArranger lobbyStage;
    private GameObject homeView;

    // 首页：复制 Home Arranger（已复制则复用）并在 CharacterAnchor 摆一个角色
    public void RefreshHome(EntityType type)
    {
        var stage = HomeStage();
        if (stage == null) return;
        slots.Clear();
        slots.Add((type, stage.CharacterAnchor));
        var got = Apply();
        homeView = got.Length > 0 ? got[0] : null;
    }

    // 首页选角：相机留在 CameraPos，原地转向展示角色的胸口锚点
    public void LookAtHome()
    {
        var stage = HomeStage();
        var cam = Tool.CameraController;
        if (stage == null || cam == null) return;
        var view = homeView;
        if (view == null) return; // 模型缺失已由 AddView 报错
        var model = view.GetComponentInChildren<EntityModelInfo>();
        var anim = view.GetComponentInChildren<EntityAnim>();
        if (model == null || anim == null) return; // 缺失已由 AddView 报错
        Vector3 focus = EntityAnchor.UpCenter.GetTransform(model, anim).position;
        cam.transform.position = stage.CameraPos.position;
        cam.transform.rotation = Quaternion.LookRotation(focus - stage.CameraPos.position);
    }

    public void ResetHomeView()
    {
        if (homeStage != null) PlaceCamera(homeStage.CameraPos);
    }

    // 大厅：复制 Lobby Arranger（已复制则复用），真人成员按成员表顺序占锚点（下标 < 0 由调用方过滤）
    public void RefreshLobby(IReadOnlyList<int> attackerIndices, IReadOnlyList<int> defenserIndices)
    {
        var stage = LobbyStage();
        if (stage == null) return;
        slots.Clear();
        FillSlots(stage.AttackerAnchors, attackerIndices, true);
        FillSlots(stage.DefenserAnchors, defenserIndices, false);
        Apply();
    }

    public void ResetLobbyView()
    {
        if (lobbyStage != null) PlaceCamera(lobbyStage.CameraPos);
    }

    // 大厅 Arranger 实例；未创建时为 null（大厅 UI 取它的 UIPos 对齐阵营面板）
    public LobbyDisplayArranger LobbyArranger => lobbyStage;

    // 进战斗前清掉两页的场景与角色（相机交给 CameraController）
    public void Hide()
    {
        ClearViews();
        DestroyHomeStage();
        DestroyLobbyStage();
    }

    protected override void UnbindEvents() => Hide();

    #region//Local
    // 首页/大厅的场景互斥：复制其中一个时销毁另一个
    private HomeDisplayArranger HomeStage()
    {
        if (homeStage != null) return homeStage;
        DestroyLobbyStage();
        if (!TryGetStagePrefab<HomeDisplayArranger>(ResourceType.HomeDisplayArranger, out var prefab))
        {
            Debug.LogError("[展示] ResourceManager 的 HomeDisplayArranger 为空（HomeArranger 预制体），首页无法布置场景");
            return null;
        }
        homeStage = UnityEngine.Object.Instantiate(prefab);
        return homeStage;
    }

    private LobbyDisplayArranger LobbyStage()
    {
        if (lobbyStage != null) return lobbyStage;
        DestroyHomeStage();
        if (!TryGetStagePrefab<LobbyDisplayArranger>(ResourceType.LobbyDisplayArranger, out var prefab))
        {
            Debug.LogError("[展示] ResourceManager 的 LobbyDisplayArranger 为空（LobbyArranger 预制体），大厅无法布置场景");
            return null;
        }
        lobbyStage = UnityEngine.Object.Instantiate(prefab);
        return lobbyStage;
    }

    // 布置图在进游戏时已由 ResourceManager 预加载，这里只取已加载的模板（按目标类型取根上的组件）
    private static bool TryGetStagePrefab<T>(ResourceType type, out T prefab) where T : Component
    {
        prefab = null;
        if (Tool.ResourceManager == null) return false;
        if (!Tool.ResourceManager.TryGetPrefab(type, 0, out var template) || template == null) return false;
        prefab = template.GetComponent<T>();
        return prefab != null;
    }

    private void DestroyHomeStage()
    {
        if (homeStage != null) UnityEngine.Object.Destroy(homeStage.gameObject);
        homeStage = null;
    }

    private void DestroyLobbyStage()
    {
        if (lobbyStage != null) UnityEngine.Object.Destroy(lobbyStage.gameObject);
        lobbyStage = null;
    }

    private void FillSlots(List<Transform> anchors, IReadOnlyList<int> indices, bool attack)
    {
        if (anchors == null || indices == null) return;
        int count = Mathf.Min(anchors.Count, indices.Count);
        for (int i = 0; i < count; i++)
            slots.Add((attack ? EntityType.Attack(indices[i]) : EntityType.Defense(indices[i]), anchors[i]));
    }

    // 增量刷新：同类型视图复用（只挪锚点、不重建），多出来的销毁，缺的才新建（新建才播登场动画）
    private GameObject[] Apply()
    {
        foreach (var list in pool.Values) list.Clear();
        for (int i = 0; i < views.Count; i++)
        {
            if (!pool.TryGetValue(views[i].type, out var list))
            {
                list = new List<GameObject>();
                pool[views[i].type] = list;
            }
            list.Add(views[i].go);
        }
        views.Clear();

        var result = new GameObject[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            var (type, anchor) = slots[i];
            GameObject go = null;
            if (pool.TryGetValue(type, out var list) && list.Count > 0)
            {
                int last = list.Count - 1;
                go = list[last];
                list.RemoveAt(last);
                if (go != null)
                {
                    var drop = go.GetComponent<SpawnDrop>();
                    if (drop != null) drop.SnapToTarget(); // 复用者若还在下落，先落地再改锚点
                    go.transform.SetPositionAndRotation(anchor.position, anchor.rotation);
                }
            }
            else
            {
                go = AddView(type, anchor);
            }
            if (go == null) continue;
            views.Add(new ViewEntry { type = type, go = go });
            result[i] = go;
        }

        // 未匹配上的旧视图全部销毁
        foreach (var list in pool.Values)
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null) UnityEngine.Object.Destroy(list[i]);
        return result;
    }

    // 角色模型：复制预制体并按战斗客户端的口径初始化动画（无 EntityData，攻击帧回调不传）
    private GameObject AddView(EntityType type, Transform anchor)
    {
        if (anchor == null) return null;
        if (Tool.ResourceManager == null || !Tool.ResourceManager.TryGetGraphic(type, out var prefab) || prefab == null)
        {
            Debug.LogError($"[展示] 缺少角色图形：{type}");
            return null;
        }
        var go = UnityEngine.Object.Instantiate(prefab, anchor.position, anchor.rotation);

        var anim = go.GetComponentInChildren<EntityAnim>();
        if (anim == null)
        {
            Debug.LogError($"[展示] 角色模型缺少 EntityAnim，动画未初始化：{type}");
            return go;
        }
        anim.Init(null);
        // 武器编号取实体配表，动作集与武器显示类型取武器表，与战斗客户端同口径
        int weaponId = EntityConfig.GetByCategory(type).weaponId ?? -1;
        if (weaponId < 0)
        {
            Debug.LogError($"[展示] {type} 没有配武器编号：动作集与武器显示类型都取自 weapon.csv");
            return go;
        }
        var weapon = WeaponConfig.Get(weaponId);
        anim.SetType(weapon.animType);
        anim.SetWeapon(weaponId, weapon.weaponDisplay);
        anim.DoSpawn(); // 登场动画：首页与大厅建视图时各播一次，与战斗内 EntityData 的口径一致
        // 登场下落：从锚点上方匀速落到锚点，高度与时长取 global_variables
        go.AddComponent<SpawnDrop>().Play(anchor.position,
            GlobalVariable.F(global_variablesEnum.arranger_spawn_offset),
            GlobalVariable.F(global_variablesEnum.arranger_spawn_fall_duration));
        return go;
    }

    // 首页/大厅没有跟随目标，相机只按机位摆一次；清掉跟随点防止战斗遗留的目标继续拽相机
    private static void PlaceCamera(Transform cameraPos)
    {
        var cam = Tool.CameraController;
        if (cam == null || cameraPos == null) return;
        cam.SetLookTarget(null);
        cam.transform.SetPositionAndRotation(cameraPos.position, cameraPos.rotation);
    }

    private void ClearViews()
    {
        for (int i = 0; i < views.Count; i++)
        {
            if (views[i].go != null) UnityEngine.Object.Destroy(views[i].go);
        }
        views.Clear();
        pool.Clear();
        slots.Clear();
        homeView = null;
    }
    #endregion
}
