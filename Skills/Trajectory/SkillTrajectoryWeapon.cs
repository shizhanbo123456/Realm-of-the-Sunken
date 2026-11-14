using UnityEngine;

// 贴武器：位置与朝向每帧取武器实例的实际世界位姿，近战判定球随挥刀扫过（不可见，只用于服务器判定）
public class SkillTrajectoryWeapon : BulletTrajectory
{
    private readonly ushort entityId;
    private Vector3 lastPos = Vector3.zero;
    private Quaternion rotation = Quaternion.identity;

    public SkillTrajectoryWeapon(ushort entityId)
    {
        this.entityId = entityId;
    }

    public override Vector3 Lerp(float factor)
    {
        // 武器实例随动画状态重建，不能缓存引用：每帧按实体 id 重新取
        if (BulletTrajectory.TryGetWeaponPose(entityId, out var pos, out var rot, out bool inHand) && inHand)
        {
            lastPos = pos;
            rotation = rot;
        }
        return lastPos; // 收刀（片段结束）后停在最后一次握持位置，判定球不跟到腰上
    }

    public override bool TryGetRotation(out Quaternion rot)
    {
        rot = rotation;
        return true;
    }
}
