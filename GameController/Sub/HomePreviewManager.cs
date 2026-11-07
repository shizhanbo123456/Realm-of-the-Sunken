using System.Collections.Generic;
using UnityEngine;

// 首页/大厅的角色场景展示：摆所选角色模型，摆相机机位与朝向
public class HomePreviewManager : ClientSubManager
{
    private readonly List<GameObject> views = new();

    // 预览模型未做 EntityAnim.Init（无 EntityData），UpCenter 会走非骨骼分支 = 模型高度的 0.75
    private EntityModelInfo attackerModel;
    private EntityModelInfo defenserModel;

    // 首页：只摆当前选中的攻/守各一个（机位与朝向由 ResetHomeView / LookAtHome 控制）
    public void RefreshHome(int attackIndex, int defenseIndex)
    {
        ClearViews();
        var arranger = HomeArranger();
        if (arranger == null) return;
        attackerModel = AddView(EntityType.Attack(attackIndex), arranger.AttackerAnchor);
        defenserModel = AddView(EntityType.Defense(defenseIndex), arranger.DefenserAnchor);
    }

    // 首页选角：相机留在 CameraPos，原地转向该阵营角色的胸口锚点
    public void LookAtHome(PlayerCampType camp)
    {
        var arranger = HomeArranger();
        var cam = Tool.CameraController;
        if (arranger == null || cam == null) return;
        var model = camp == PlayerCampType.Defense ? defenserModel : attackerModel;
        if (model == null) return; // 模型缺失已由 AddView 报错
        Vector3 focus = EntityAnchor.UpCenter.GetTransform(model, null).position;
        cam.transform.position = arranger.CameraPos.position;
        cam.transform.rotation = Quaternion.LookRotation(focus - arranger.CameraPos.position);
    }

    public void ResetHomeView()
    {
        var arranger = HomeArranger();
        if (arranger != null) PlaceCamera(arranger.CameraPos);
    }

    // 大厅：真人成员按成员表顺序占锚点（下标 < 0 由调用方过滤）
    public void RefreshLobby(IReadOnlyList<int> attackerIndices, IReadOnlyList<int> defenserIndices)
    {
        ClearViews();
        var arranger = LobbyArranger();
        if (arranger == null) return;
        FillAnchors(arranger.AttackerAnchors, attackerIndices, true);
        FillAnchors(arranger.DefenserAnchors, defenserIndices, false);
    }

    public void ResetLobbyView()
    {
        var arranger = LobbyArranger();
        if (arranger != null) PlaceCamera(arranger.CameraPos);
    }

    // 进战斗前清掉预览模型（相机交给 CameraController）
    public void Hide() => ClearViews();

    protected override void UnbindEvents() => Hide();

    #region//Local
    private static HomeDisplayArranger HomeArranger()
    {
        var arranger = Tool.AssetsManager != null ? Tool.AssetsManager.homeDisplayArranger : null;
        if (arranger == null) Debug.LogError("[展示] AssetsManager 未挂 homeDisplayArranger，首页无法摆放角色");
        return arranger;
    }

    private static LobbyDisplayArranger LobbyArranger()
    {
        var arranger = Tool.AssetsManager != null ? Tool.AssetsManager.lobbyDisplayArranger : null;
        if (arranger == null) Debug.LogError("[展示] AssetsManager 未挂 lobbyDisplayArranger，大厅无法摆放角色");
        return arranger;
    }

    private void FillAnchors(List<Transform> anchors, IReadOnlyList<int> indices, bool attack)
    {
        if (anchors == null || indices == null) return;
        int count = Mathf.Min(anchors.Count, indices.Count);
        for (int i = 0; i < count; i++)
            AddView(attack ? EntityType.Attack(indices[i]) : EntityType.Defense(indices[i]), anchors[i]);
    }

    private EntityModelInfo AddView(EntityType type, Transform anchor)
    {
        if (anchor == null) return null;
        if (Tool.AssetsManager == null || !Tool.AssetsManager.TryGetGraphic(type, out var prefab) || prefab == null)
        {
            Debug.LogError($"[展示] 缺少角色图形：{type}");
            return null;
        }
        var go = UnityEngine.Object.Instantiate(prefab, anchor.position, anchor.rotation);
        views.Add(go);
        var model = go.GetComponentInChildren<EntityModelInfo>();
        if (model == null) Debug.LogError($"[展示] 角色模型缺少 EntityModelInfo，无法算胸口锚点：{type}");
        return model;
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
        attackerModel = null;
        defenserModel = null;
    }
    #endregion
}
