using UnityEngine;

public abstract class BulletTrajectory
{
    public abstract Vector3 Lerp(float factor);

    public virtual Vector3 Start => Lerp(0f);

    public virtual Vector3 End => Lerp(1f);

    // 跟随类轨迹自带朝向（贴武器 / 贴骨骼）：返回 true 时命中球偏移按该朝向摆放，不再按运动切向推算
    public virtual bool TryGetRotation(out Quaternion rotation)
    {
        rotation = Quaternion.identity;
        return false;
    }

    public float Duration = 1f;

    public static bool TryGetEntityPosition(ushort entityId, out Vector3 pos)
    {
        return TryGetEntityTransform(entityId, out pos, out _);
    }

    public static bool TryGetEntityTransform(ushort entityId, out Vector3 pos, out Quaternion rot)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        // 服务器：实体容器
        if (BattleManager.EntityContainer.Entities.TryGetObject(entityId, out var entity) &&
            entity != null)
        {
            pos = entity.transform.position;
            rot = entity.transform.rotation;
            return true;
        }
        // 客户端：表现物体
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players != null)
        {
            return players.TryGetEntityTransform(entityId, out pos, out rot);
        }
        return false;
    }

    // 武器实例的实际位姿（近战判定球按它摆放，而不是按手部骨骼/挂点）：服务器查实体，客户端查表现视图
    // inHand = 握在手上；收起时判定球停在原位、不跟着跑到腰上
    public static bool TryGetWeaponPose(ushort entityId, out Vector3 pos, out Quaternion rot, out bool inHand)
    {
        pos = Vector3.zero;
        rot = Quaternion.identity;
        inHand = false;
        EntityAnim anim = null;
        if (BattleManager.EntityContainer.Entities.TryGetObject(entityId, out var entity) && entity != null)
        {
            anim = entity.anim;
        }
        else
        {
            var view = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers.GetView(entityId) : null;
            if (view != null) anim = view.anim;
        }
        Transform weapon = anim != null ? anim.WeaponTransform : null;
        if (weapon == null) return false;
        pos = weapon.position;
        rot = weapon.rotation;
        inHand = anim.WeaponInHand;
        return true;
    }
}
