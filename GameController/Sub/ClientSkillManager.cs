using System.Collections.Generic;
using Ros.Config;
using Ros.Skill.Utils;
using Ros.Transport;
using UnityEngine;

// 客户端技能表现数据：服务器下发的持有表（选择面板用）与槽位冷却快照（技能栏用），本地不算库存
public class ClientSkillManager : ClientSubManager
{
    private readonly List<int> heldSkills = new();
    private readonly List<int> activeSkills = new();   // 槽位技能，顺序即按键顺序，来自服务器快照

    // 根戳由服务器下发，读数只是 (服务器时间 − 根戳) / CD，与服务器时钟对齐
    private readonly SkillRecharge recharge = new(() => NetworkManager.ClientToServerTime(Time.time));

    private int slotCapacity;
    private int selectedSlot;   // 滚轮选中的槽位（右键释放它），槽位表变化时夹紧

    public IReadOnlyList<int> HeldSkills => heldSkills;

    public IReadOnlyList<int> ActiveSkills => activeSkills;

    public int SlotCapacity => slotCapacity;

    public int SelectedSlot => selectedSlot;

    public SkillRecharge Recharge => recharge;

    #region 打击感（受击震颤 / 近战命中震颤 + 卡肉）
    // 走 SkillBase.StrikeWeapon（贴武器实例命中球判定）的技能：服务器加新的近战技能时这里要同步
    private static readonly HashSet<int> meleeWeaponSkills = new() { GlobalConfig.unarmed_punch };

    private const float HitStopSeconds = 0.045f;  // 单次命中冻结自己挥砍的时长
    private const float MeleeRequestGrace = 2f;   // 施法请求到攻击帧的容差，超时说明这次请求没落地

    private EntityAnim meleeAnim;   // 已挂钩的本地视图动画，换视图时重挂
    private int meleeSkillId = -1;  // 最近一次近战施法请求，攻击帧取用后清空
    private float meleeRequestTime;
    private float meleeWindowEnd;   // 命中判定窗的结束时刻，0 = 未开窗
    private float meleeScale = 1f;
    private bool meleeSampled;
    private Vector3 meleeLastPos;
    private Quaternion meleeLastRot = Quaternion.identity;
    private Transform meleeWeapon;
    private BulletVfxInfo meleeHits;
    #endregion

    #region 生命周期
    public override void Init(ClientLogicManager owner)
    {
        base.Init(owner);
        slotCapacity = ResolveSlotCapacity();
    }

    // 每局进战斗页时清空：持有表与槽位都随对局重建，等服务器重发
    public void ResetBattle()
    {
        heldSkills.Clear();
        activeSkills.Clear();
        recharge.Clear();
        slotCapacity = ResolveSlotCapacity();
        selectedSlot = 0;
        meleeAnim = null;
        meleeSkillId = -1;
        meleeWindowEnd = 0f;
        meleeSampled = false;
    }
    #endregion

    #region 持有与技能栏
    // 服务器下发的持有表（全量替换）：只影响选择面板，槽位与冷却以服务器快照为准
    public void SetHeldSkills(List<int> ids)
    {
        if (slotCapacity <= 0) slotCapacity = ResolveSlotCapacity(); // 会话晚于初始化建立时补一次
        heldSkills.Clear();
        if (ids != null) heldSkills.AddRange(ids);

        for (int i = activeSkills.Count - 1; i >= 0; i--)
        {
            if (!heldSkills.Contains(activeSkills[i])) activeSkills.RemoveAt(i);
        }
        EventManager.TrigEvent(ClientEvent.OnSkillBarRefresh);
    }

    // 增量新增（掉落 / GM 下发）：已持有则忽略
    public void AddHeldSkill(int skillId)
    {
        if (skillId < 0 || heldSkills.Contains(skillId)) return;
        heldSkills.Add(skillId);
        EventManager.TrigEvent(ClientEvent.OnSkillBarRefresh);
    }

    // 服务器快照：槽位技能 + cd 根戳，一次覆盖本地显示状态（不每帧同步）
    public void ApplyRecharge(IReadOnlyList<int> skillIds, IReadOnlyList<float> roots)
    {
        activeSkills.Clear();
        if (skillIds != null) activeSkills.AddRange(skillIds);
        recharge.SetRoots(skillIds, roots);
        ClampSelectedSlot();
        EventManager.TrigEvent(ClientEvent.OnSkillBarRefresh);
    }

    // 选择面板关闭时上行：服务器校验后回推快照，本地不预演（避免闪现错误库存）
    public bool TrySetActiveSkills(List<int> ids)
    {
        if (ids == null) return false;
        foreach (var skillId in ids)
        {
            if (!heldSkills.Contains(skillId)) return false;
        }
        if (ids.Count > slotCapacity) return false;
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendSkillSelection(ids);
        return true;
    }

    public bool IsActive(int skillId) => activeSkills.Contains(skillId);

    public int GetSlotSkillId(int slot)
    {
        return slot >= 0 && slot < activeSkills.Count ? activeSkills[slot] : -1;
    }
    #endregion

    #region 释放
    // 滚轮切槽位：按角色槽位数循环（含尾部空槽），空槽可选中但右键不发包
    public void SelectSlotDelta(int delta)
    {
        if (slotCapacity <= 0) return;
        int next = (selectedSlot + delta) % slotCapacity;
        selectedSlot = next < 0 ? next + slotCapacity : next;
        EventManager.TrigEvent(ClientEvent.OnSkillBarRefresh);
    }

    // 右键：释放当前选中槽位
    public void CastSelected()
    {
        CastSlot(selectedSlot);
    }

    // 技能槽位 → 技能 id 由服务器下发的槽位表决定，空槽不发包；无战斗实体（观战/未开局）一律不发
    public void CastSlot(int slot)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession || loading.OwnEntityId == 0) return;
        int skillId = GetSlotSkillId(slot);
        if (skillId < 0) return;
        MarkCastRequest(skillId);
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendSkillCastInput(skillId);
    }

    // 普攻（鼠标左键）：移动与静止共用一个技能
    public void CastUnarmed()
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession || loading.OwnEntityId == 0) return; // 无战斗实体（观战/未开局）不发
        MarkCastRequest(GlobalConfig.unarmed_punch);
        if (Tool.NetworkManager != null) Tool.NetworkManager.SendSkillCastInput(GlobalConfig.unarmed_punch);
    }
    #endregion

    #region 打击感
    public override void Tick(float deltaTime)
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession || loading.OwnEntityId == 0)
        {
            meleeAnim = null;
            meleeWindowEnd = 0f;
            return;
        }

        var view = logic.EntityPlayers.GetView(loading.OwnEntityId);
        if (view == null || view.anim == null)
        {
            meleeAnim = null;
            meleeWindowEnd = 0f;
            return;
        }
        // 本地视图换过（复活重建）：重挂攻击帧回调。客户端只有判断命中这一处占用 onAttack
        if (view.anim != meleeAnim)
        {
            meleeAnim = view.anim;
            meleeAnim.onAttack = OnOwnAttackFrame;
        }

        if (meleeWindowEnd <= 0f) return;
        if (Time.time >= meleeWindowEnd)
        {
            meleeWindowEnd = 0f;
            return;
        }
        SampleMeleeHit(view);
    }

    protected override void BindEvents()
    {
        EventManager.AddEvent<SCCombatLabel>(ClientEvent.OnCombatLabelDisplay, OnCombatLabel);
    }

    protected override void UnbindEvents()
    {
        EventManager.RemoveEvent<SCCombatLabel>(ClientEvent.OnCombatLabelDisplay, OnCombatLabel);
    }

    // 受击震颤：飘字只带受击实体 id，命中类是唯一来源；DoT/闪避/失效/连击保护/反伤一律不震
    private void OnCombatLabel(SCCombatLabel label)
    {
        if (label == null) return;
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession || loading.OwnEntityId == 0) return;
        if (label.targetId != loading.OwnEntityId) return;

        float trauma;
        switch ((CombatLabelType)label.type)
        {
            case CombatLabelType.Normal: trauma = GlobalVariable.F(global_variablesEnum.camera_shake_hurt_normal); break;
            case CombatLabelType.Crit: trauma = GlobalVariable.F(global_variablesEnum.camera_shake_hurt_crit); break;
            case CombatLabelType.Shield: trauma = GlobalVariable.F(global_variablesEnum.camera_shake_hurt_shield); break;
            default: return;
        }
        if (Tool.CameraController != null) Tool.CameraController.AddTrauma(trauma);
    }

    // 本地视图的攻击帧，与服务器同一归一化时刻：只有走武器判定的近战技能开窗
    private void OnOwnAttackFrame(EntityAnim.AttackType type)
    {
        if (meleeSkillId < 0) return;
        bool recent = Time.time - meleeRequestTime <= MeleeRequestGrace;
        if (!recent || !meleeWeaponSkills.Contains(meleeSkillId))
        {
            meleeSkillId = -1;
            return;
        }

        meleeScale = ResolveHitScale(meleeSkillId);
        meleeWindowEnd = Time.time + GlobalVariable.F(global_variablesEnum.melee_bullet_life);
        meleeSampled = false;
        meleeSkillId = -1; // 一次请求只开一层窗，重击帧不叠加
    }
    #endregion

    #region 表现
    // 服务器只在攻击帧下发波次，客户端收到即播；wave 特效不走 anim.onAttack（那个回调留给命中判断）
    public void OnSkillCast(SCSkillDisplayContext context)
    {
        if (context == null) return;
        SkillManager.PlayWave(context);
    }
    #endregion

    #region//Local
    // 选中槽位不能超出角色槽位数（槽位表变化、角色属性晚到都走这里）
    private void ClampSelectedSlot()
    {
        int max = Mathf.Max(0, slotCapacity - 1);
        if (selectedSlot > max) selectedSlot = max;
        if (selectedSlot < 0) selectedSlot = 0;
    }

    // 技能栏槽位数 = 本局角色属性（与服务器同一列 entity.csv 第 9 列）
    private static int ResolveSlotCapacity()
    {
        var loading = ClientLoadingManager.Current;
        if (loading == null || !loading.HasSession) return 0;
        int saveIndex = loading.Camp switch
        {
            EntityCamp.Attack => ClientSelection.selectedAttackIndex,
            EntityCamp.Defense => GlobalConfig.attack_character_count + ClientSelection.selectedDefenseIndex,
            _ => -1,
        };
        if (saveIndex < 0 || Tool.InfoManager == null || Tool.SaveManager == null) return 0;
        return Mathf.Max(0, Tool.InfoManager.GetPlayerCharacterInfo(saveIndex).GetAttribute().weaponSlotCount);
    }

    // 记下这次施法请求的技能，攻击帧据此判断要不要开命中判定窗
    private void MarkCastRequest(int skillId)
    {
        meleeSkillId = meleeWeaponSkills.Contains(skillId) ? skillId : -1;
        meleeRequestTime = Time.time;
    }

    // 与服务器 SkillBase.VfxScale(0) 同口径：skill 表第 9 列第 0 项，没配 = 原大小
    private static float ResolveHitScale(int skillId)
    {
        var sizes = SkillConfig.Get(skillId).bulletSizes;
        return sizes.Count > 0 ? sizes[0] : 1f;
    }

    // 命中球取手上武器实例的 BulletVfxInfo（与服务器同一预制体）；VfxHitSource 走 AssetsManager，客户端拿不到
    private BulletVfxInfo ResolveWeaponHits()
    {
        Transform weapon = meleeAnim != null ? meleeAnim.WeaponTransform : null;
        if (weapon == null) return null;
        if (weapon == meleeWeapon) return meleeHits;
        meleeWeapon = weapon;
        meleeHits = weapon.GetComponent<BulletVfxInfo>();
        return meleeHits;
    }

    // 按服务器的采样口径逐帧取武器位姿做扫掠，纯表现触发，不结算任何伤害
    private void SampleMeleeHit(EntityPlayerManager.ClientEntityView view)
    {
        if (!BulletTrajectory.TryGetWeaponPose(view.id, out var pos, out var rot, out bool inHand) || !inHand) return;
        var hits = ResolveWeaponHits();
        if (hits == null || hits.hits.Count == 0) return;

        if (!meleeSampled)
        {
            meleeLastPos = pos;
            meleeLastRot = rot;
            meleeSampled = true;
            return;
        }

        bool hit = false;
        for (int i = 0; i < hits.hits.Count; i++)
        {
            var h = hits.hits[i];
            Vector3 offset = h.center * meleeScale;
            Vector3 from = meleeLastPos + meleeLastRot * offset;
            Vector3 to = pos + rot * offset;
            if (OverlapHostile(from, to, h.radius * meleeScale, view.id, view.type.category))
            {
                hit = true;
                break;
            }
        }

        meleeLastPos = pos;
        meleeLastRot = rot;
        if (hit) TriggerMeleeHit();
    }

    // 与服务器的 EntityPhysics.OverlapCapsuleXZ 同口径：纯 XZ 的点到线段距离 < 判定球半径 + 目标胶囊半径
    private bool OverlapHostile(Vector3 from, Vector3 to, float radius, ushort ownId, EntityCategory ownCategory)
    {
        foreach (var view in logic.EntityPlayers.AllViews)
        {
            if (view == null || view.id == ownId || view.capsule == null) continue;
            if (!EntityCampUtil.IsHostile(ownCategory, view.camp)) continue;

            Transform shape = view.capsule.transform;
            Vector3 scale = shape.lossyScale;
            float targetRadius = view.capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            Vector3 center = shape.TransformPoint(view.capsule.center);
            float sum = radius + targetRadius;
            if (SqrDistPointSegmentXZ(center, from, to) > sum * sum) continue;
            return true;
        }
        return false;
    }

    private static float SqrDistPointSegmentXZ(Vector3 p, Vector3 a, Vector3 b)
    {
        float ax = p.x - a.x, az = p.z - a.z;
        float bx = b.x - a.x, bz = b.z - a.z;
        float lenSqr = bx * bx + bz * bz;
        float t = lenSqr > 1e-8f ? Mathf.Clamp01((ax * bx + az * bz) / lenSqr) : 0f;
        float dx = ax - bx * t, dz = az - bz * t;
        return dx * dx + dz * dz;
    }

    // 一次挥砍只触发一次：震颤与卡肉同帧发生，卡肉时长固定（暴击由服务器结算，本地预知不到）
    private void TriggerMeleeHit()
    {
        meleeWindowEnd = 0f;
        if (Tool.CameraController != null)
            Tool.CameraController.AddTrauma(GlobalVariable.F(global_variablesEnum.camera_shake_hit_normal));
        if (meleeAnim != null) meleeAnim.BeginHitStop(HitStopSeconds);
    }
    #endregion
}
