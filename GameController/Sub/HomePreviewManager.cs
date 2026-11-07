using System.Collections.Generic;
using UnityEngine;

// 首页/大厅的角色场景展示：摆所选角色模型，摆相机机位与朝向
public class HomePreviewManager : ClientSubManager
{
    private readonly List<GameObject> views = new();

    // 首页：只摆当前选中的攻/守各一个（机位与朝向由 ResetHomeView / LookAtHome 控制）
    public void RefreshHome(int attackIndex, int defenseIndex)
    {
        ClearViews();
        var arranger = HomeArranger();
        if (arranger == null) return;
        AddView(EntityType.Attack(attackIndex), arranger.AttackerAnchor);
        AddView(EntityType.Defense(defenseIndex), arranger.DefenserAnchor);
    }

    // 首页选角：相机留在 CameraPos，原地转向该阵营角色
    public void LookAtHome(PlayerCampType camp)
    {
        var arranger = HomeArranger();
        var cam = Tool.CameraController;
        if (arranger == null || cam == null) return;
        var target = camp == PlayerCampType.Defense ? arranger.DefenserAnchor : arranger.AttackerAnchor;
        if (target == null) return;
        cam.transform.position = arranger.CameraPos.position;
        cam.transform.rotation = Quaternion.LookRotation(target.position - arranger.CameraPos.position);
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

    private void AddView(EntityType type, Transform anchor)
    {
        if (anchor == null) return;
        if (Tool.AssetsManager == null || !Tool.AssetsManager.TryGetGraphic(type, out var prefab) || prefab == null)
        {
            Debug.LogError($"[展示] 缺少角色图形：{type}");
            return;
        }
        views.Add(UnityEngine.Object.Instantiate(prefab, anchor.position, anchor.rotation));
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
    }
    #endregion
}
