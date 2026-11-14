using System;
using Ros.Skill;
using UnityEngine;

// 全项目唯一的特效取用入口。模板经 ResourceManager 按需加载，加载完成后在回调里落到算好的位姿上：
// 定点版生成即播放并按时长销毁，轨迹版交给 BulletPlayer 驱动，跟随类（护盾/Buff/光环）由持有者决定何时销毁。
public class VfxManager : MonoBehaviour
{
    private void Awake()
    {
        Tool.VfxManager = this;
    }

    #region 取实例（按需加载）
    // position/rotation 必须由调用方在请求时算好：粒子在实例化那一帧就会喷，晚摆会留下原点残影
    public void LoadVfx(ResourceType type, int index, Vector3 position, Quaternion rotation, Action<GameObject> onSpawned)
    {
        if (Tool.ResourceManager == null)
        {
            Debug.LogError("[Vfx] 缺少 ResourceManager：无法加载特效");
            return;
        }
        Tool.ResourceManager.Load(type, index, position, rotation, onSpawned);
    }

    public static bool TryResourceOf(SkillVfxKind kind, out ResourceType type)
    {
        switch (kind)
        {
            case SkillVfxKind.Bullet: type = ResourceType.BulletVFX; return true;
            case SkillVfxKind.Shield: type = ResourceType.ShieldVFX; return true;
            case SkillVfxKind.RangeMagic: type = ResourceType.RangeMagicVFX; return true;
            case SkillVfxKind.MagicCircle: type = ResourceType.MagicCircleVFX; return true;
            case SkillVfxKind.Buff: type = ResourceType.BuffVFX; return true;
            case SkillVfxKind.Slash: type = ResourceType.SlashVFX; return true;
            case SkillVfxKind.Projection: type = ResourceType.ProjectionVFX; return true;
            case SkillVfxKind.Weapon: type = ResourceType.WeaponPrefabs; return true;
            default: type = default; return false;
        }
    }
    #endregion

    #region 播放原语（已持有实例）
    public GameObject Play(GameObject instance, float lifeTime, float scale = 0f)
    {
        if (instance == null) return null;
        ApplyScale(instance, scale);
        Destroy(instance, ResolveLife(instance, lifeTime));
        return instance;
    }

    public GameObject Play(GameObject instance, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Tangent, float scale = 0f)
    {
        if (instance == null || trajectory == null) return null;
        ApplyScale(instance, scale);
        float life = lifeTime > 0f ? lifeTime : trajectory.Duration;
        // 跟随实体的持续表现（护盾/Buff/光环）由持有者决定何时销毁，不吃资产存活时长
        BulletPlayer.Create(instance, trajectory, trajectory is SkillTrajectoryFollow ? life : ResolveLife(instance, life), rotation);
        return instance;
    }

    // 存活时长：资产填了就用资产的；<=0（-1 = 资产自身不主动清除）用调用方给的时长，再退回 1 秒防泄漏
    private static float ResolveLife(GameObject instance, float lifeTime)
    {
        float asset = BulletVfxInfo.LifeOf(instance);
        if (asset > 0f) return asset;
        return lifeTime > 0f ? lifeTime : 1f;
    }

    // 特效资产的位姿与缩放统一是 (0,0,0)/(1,1,1)，倍率直接写成根节点缩放（0 = 不缩放）
    private static void ApplyScale(GameObject obj, float scale)
    {
        if (obj == null || scale <= 0f || Mathf.Approximately(scale, 1f)) return;
        obj.transform.localScale = Vector3.one * scale;
    }
    #endregion

    #region 按下标播放（内部按需加载）
    public void PlayBulletVFX(int index, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.BulletVFX, index, pos, rot, lifeTime, scale);
    }

    public void PlayBulletVFX(int index, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Tangent, float scale = 0f)
    {
        PlayByIndex(ResourceType.BulletVFX, index, trajectory, lifeTime, rotation, scale);
    }

    // 传武器编号（entity.csv「武器编号」）；武器在进游戏时已预加载，回调是同步的
    public void PlayWeaponVFX(int weaponId, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.WeaponPrefabs, weaponId, pos, rot, lifeTime, scale);
    }

    public void PlayWeaponVFX(int weaponId, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Tangent, float scale = 0f)
    {
        PlayByIndex(ResourceType.WeaponPrefabs, weaponId, trajectory, lifeTime, rotation, scale);
    }

    public void PlayShieldVFX(int index, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.ShieldVFX, index, pos, rot, lifeTime, scale);
    }

    public void PlayShieldVFX(int index, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Constant, float scale = 0f)
    {
        PlayByIndex(ResourceType.ShieldVFX, index, trajectory, lifeTime, rotation, scale);
    }

    public void PlayRangeMagicVFX(int index, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.RangeMagicVFX, index, pos, rot, lifeTime, scale);
    }

    // 轨迹版语义：取轨迹终点（Lerp(1)）落地定点播放，朝向固定 identity
    public void PlayRangeMagicVFX(int index, BulletTrajectory trajectory, float lifeTime = 0f, float scale = 0f)
    {
        PlayAtTrajectoryEnd(ResourceType.RangeMagicVFX, index, trajectory, lifeTime, scale);
    }

    public void PlayMagicCircleVFX(int index, Vector3 pos, float duration, float scale = 0f)
    {
        PlayByIndex(ResourceType.MagicCircleVFX, index, pos, Quaternion.identity, duration, scale);
    }

    // 轨迹版语义：取轨迹终点（Lerp(1)）落地定点播放，朝向固定 identity
    public void PlayMagicCircleVFX(int index, BulletTrajectory trajectory, float duration = 0f, float scale = 0f)
    {
        PlayAtTrajectoryEnd(ResourceType.MagicCircleVFX, index, trajectory, duration, scale);
    }

    public void PlayBuffVFX(int index, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Constant, float scale = 0f)
    {
        PlayByIndex(ResourceType.BuffVFX, index, trajectory, lifeTime, rotation, scale);
    }

    public void PlayBuffVFX(int index, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.BuffVFX, index, pos, rot, lifeTime, scale);
    }

    public void PlayHitVFX(int index, Vector3 pos, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.HitVFX, index, pos, Quaternion.identity, lifeTime, scale);
    }

    public void PlaySlashVFX(int index, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Tangent, float scale = 0f)
    {
        PlayByIndex(ResourceType.SlashVFX, index, trajectory, lifeTime, rotation, scale);
    }

    public void PlaySlashVFX(int index, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.SlashVFX, index, pos, rot, lifeTime, scale);
    }

    public void PlayProjectionVFX(int index, BulletTrajectory trajectory, float lifeTime = 0f,
        BulletPlayer.RotationMode rotation = BulletPlayer.RotationMode.Tangent, float scale = 0f)
    {
        PlayByIndex(ResourceType.ProjectionVFX, index, trajectory, lifeTime, rotation, scale);
    }

    public void PlayProjectionVFX(int index, Vector3 pos, Quaternion rot, float lifeTime, float scale = 0f)
    {
        PlayByIndex(ResourceType.ProjectionVFX, index, pos, rot, lifeTime, scale);
    }
    #endregion

    #region//Local
    private void PlayByIndex(ResourceType type, int index, Vector3 pos, Quaternion rot, float lifeTime, float scale)
    {
        LoadVfx(type, index, pos, rot, obj => Play(obj, lifeTime, scale));
    }

    private void PlayByIndex(ResourceType type, int index, BulletTrajectory trajectory, float lifeTime,
        BulletPlayer.RotationMode rotation, float scale)
    {
        if (trajectory == null) return;
        LoadVfx(type, index, trajectory.Lerp(0f), Quaternion.identity,
            obj => Play(obj, trajectory, lifeTime, rotation, scale));
    }

    // 轨迹版语义：取轨迹终点（Lerp(1)）落地定点播放
    private void PlayAtTrajectoryEnd(ResourceType type, int index, BulletTrajectory trajectory, float lifeTime, float scale)
    {
        if (trajectory == null) return;
        float life = lifeTime > 0f ? lifeTime : trajectory.Duration;
        PlayByIndex(type, index, trajectory.End, Quaternion.identity, life, scale);
    }
    #endregion
}
