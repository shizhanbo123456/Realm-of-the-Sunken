using System.Collections.Generic;
using UnityEngine;

// 首页/大厅的场景展示：复制对应的 Arranger 预制体（含场景与锚点），摆所选角色并摆相机
public class HomePreviewManager : ClientSubManager
{
    private readonly List<GameObject> views = new();
    private HomeDisplayArranger homeStage;
    private LobbyDisplayArranger lobbyStage;
    private GameObject attackerView;
    private GameObject defenserView;

    // 首页：复制 Home Arranger（已复制则复用）并只摆当前选中的攻/守各一个
    public void RefreshHome(int attackIndex, int defenseIndex)
    {
        var stage = HomeStage();
        if (stage == null) return;
        ClearViews();
        attackerView = AddView(EntityType.Attack(attackIndex), stage.AttackerAnchor);
        defenserView = AddView(EntityType.Defense(defenseIndex), stage.DefenserAnchor);
    }

    // 首页选角：相机留在 CameraPos，原地转向该阵营角色的胸口锚点
    public void LookAtHome(PlayerCampType camp)
    {
        var stage = HomeStage();
        var cam = Tool.CameraController;
        if (stage == null || cam == null) return;
        var view = camp == PlayerCampType.Defense ? defenserView : attackerView;
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
        ClearViews();
        FillAnchors(stage.AttackerAnchors, attackerIndices, true);
        FillAnchors(stage.DefenserAnchors, defenserIndices, false);
    }

    public void ResetLobbyView()
    {
        if (lobbyStage != null) PlaceCamera(lobbyStage.CameraPos);
    }

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
        var prefab = Tool.AssetsManager != null ? Tool.AssetsManager.homeDisplayArranger : null;
        if (prefab == null)
        {
            Debug.LogError("[展示] AssetsManager 未挂 homeDisplayArranger（HomeArranger 预制体），首页无法布置场景");
            return null;
        }
        homeStage = UnityEngine.Object.Instantiate(prefab);
        return homeStage;
    }

    private LobbyDisplayArranger LobbyStage()
    {
        if (lobbyStage != null) return lobbyStage;
        DestroyHomeStage();
        var prefab = Tool.AssetsManager != null ? Tool.AssetsManager.lobbyDisplayArranger : null;
        if (prefab == null)
        {
            Debug.LogError("[展示] AssetsManager 未挂 lobbyDisplayArranger（LobbyArranger 预制体），大厅无法布置场景");
            return null;
        }
        lobbyStage = UnityEngine.Object.Instantiate(prefab);
        return lobbyStage;
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

    private void FillAnchors(List<Transform> anchors, IReadOnlyList<int> indices, bool attack)
    {
        if (anchors == null || indices == null) return;
        int count = Mathf.Min(anchors.Count, indices.Count);
        for (int i = 0; i < count; i++)
            AddView(attack ? EntityType.Attack(indices[i]) : EntityType.Defense(indices[i]), anchors[i]);
    }

    // 角色模型：复制预制体并按战斗客户端的口径初始化动画（无 EntityData，攻击帧回调不传）
    private GameObject AddView(EntityType type, Transform anchor)
    {
        if (anchor == null) return null;
        if (Tool.AssetsManager == null || !Tool.AssetsManager.TryGetGraphic(type, out var prefab) || prefab == null)
        {
            Debug.LogError($"[展示] 缺少角色图形：{type}");
            return null;
        }
        var go = UnityEngine.Object.Instantiate(prefab, anchor.position, anchor.rotation);
        views.Add(go);

        var anim = go.GetComponentInChildren<EntityAnim>();
        if (anim == null)
        {
            Debug.LogError($"[展示] 角色模型缺少 EntityAnim，动画未初始化：{type}");
            return go;
        }
        anim.Init(null);
        // 武器编号取实体配表，动作集与武器显示类型取武器表，与战斗客户端同口径
        int weaponId = ConfigManager.EntityConfig.GetByCategory(type).weaponId;
        if (weaponId < 0)
        {
            Debug.LogError($"[展示] {type} 没有配武器编号：动作集与武器显示类型都取自 weapon.csv");
            return go;
        }
        var weapon = ConfigManager.WeaponConfig.Get(weaponId);
        anim.SetType(weapon.animType);
        anim.SetWeapon(weaponId, weapon.weaponDisplay);
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
            if (views[i] != null) UnityEngine.Object.Destroy(views[i]);
        }
        views.Clear();
        attackerView = null;
        defenserView = null;
    }
    #endregion
}
