using UnityEngine;

// 初始界面预览：地形不提供攻/守站位与展示机位锚点，暂不生成预览模型（接口保留，后续接入锚点后再实现）
public class HomePreviewManager : ClientSubManager
{
    public void Refresh(int attackIndex, int defenseIndex) { }

    public void Hide() { }

    protected override void UnbindEvents() { }
}
