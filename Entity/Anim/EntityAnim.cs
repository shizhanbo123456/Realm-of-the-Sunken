using Ros.Transport;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EntityAnim : MonoBehaviour
{
    private const string key_characterType = "CharacterType";
    private const string key_spawn = "Spawn";
    private const string key_jump = "Jump";
    private const string key_moving = "Moving";
    private const string key_inAir = "InAir";
    private const string key_slide = "Slide";
    private const string key_doAttack = "Attack";
    private const string key_attackId = "AttackId";
    private const string key_hit = "Hit";
    private const string key_doHit = "DoHit";
    public enum CharcterAnimType
    {
        Spear=0,
        Sword=1
    }
    public enum AnimState
    {
        Spawn,
        Motion,
        Attack,
        Hit,
    }
    public enum MotionType
    {
        Idle,
        Run,
        Jump,
        Slide
    }
    public enum AttackType
    {
        Combo=0,
        Attack_Hand=10,
        Attack_Weapon=11,
        Attack_AirDrop=20,
        Attack_AirFront=21,
        Kick=30,
        Scream=31,
        Mega_Short=40,
        Mega_Middle=41,
        Mega_Long=42,
        Weapon_A=50,
        Weapon_B=51,
        Weapon_C=52,
        Weapon_D=53,
    }
    public enum WeaponPos
    {
        Hide,   // 不显示
        Show,   // 收起：长枪背在背上，刀收在腰间
        Hand,   // 拿在手上
    }
    public enum WeaponDisplayType
    {
        Spear,      // 长枪
        Sword,      // 刀
        DualSword,  // 双刀
    }
    private AnimState currentState = AnimState.Motion;
    public AnimState CurrentState
    {
        get=>currentState;
        set
        {
            if (currentState == value) return;
            if (OnStateChange != null) OnStateChange.Invoke(currentState, value);
            currentState = value;
        }
    }
    private EntityData data;
    [HideInInspector]public EntityAnimData animData;


    private int currentAnimId = -1;

    #region//事件
    public Action<AttackType> onAttack;//动画中的攻击事件回调
    public Action<AnimState, AnimState> OnStateChange;
    #endregion

    private List<Animator> animators;
    private Animator mainAnimator;

    public Animator MainAnimator => mainAnimator;


    public void Init(EntityData data)
    {
        this.data = data;

        // 参数包复位：重复 Init（如视图重建）时清掉上一轮残留的持久参数
        paramPack = AnimParamPack.Default;

        animData = GetComponent<EntityAnimData>();
        if (animData == null) Debug.LogError($"{gameObject.name}未挂载animData");
        modelInfo = GetComponentInChildren<EntityModelInfo>();
        if (modelInfo == null) Debug.LogError($"{gameObject.name}未挂载 EntityModelInfo：武器挂点解析不了");

        animators = new();
        if(TryGetComponent<Animator>(out var anim))
        {
            animators.Add(anim);
        }
        for(int i = 0; i < transform.childCount;i++)
        {
            if(transform.GetChild(i).TryGetComponent<Animator>(out anim))
            {
                animators.Add(anim);
            }
        }
        if (animators.Count == 0)
        {
            Debug.LogError($"{gameObject.name} 中未找到任何animator");
            return;
        }
        mainAnimator = animators[0];
        for (int i = 0; i < animators.Count; i++)
        {
            Animator animator = animators[i];
            animator.runtimeAnimatorController = Tool.AssetsManager.animatorController;
            var behaviours = animator.GetBehaviours<AnimEvent>();
            foreach (var behaviour in behaviours) behaviour.Init(this, data,i==0);
        }
        // 把复位后的参数推给状态机：保证"paramPack 与 Animator 参数一致"这个前提（值比较的脏标记依赖它）
        PushParamsToAnimators();
    }

    private void PushParamsToAnimators()
    {
        foreach (var animator in animators)
        {
            animator.SetInteger(key_characterType, paramPack.characterType);
            animator.SetInteger(key_attackId, paramPack.attackId);
            animator.SetBool(key_inAir, paramPack.inAir);
            animator.SetBool(key_moving, paramPack.moving);
            animator.SetBool(key_slide, paramPack.slide);
            animator.SetInteger(key_hit, paramPack.hit);
        }
    }
    public void SetType(CharcterAnimType type)
    {
        SetIntParam(ref paramPack.characterType, (int)type, key_characterType);
    }

    public void NotifyStateEnter(int animId, AnimState state)
    {
        currentAnimId = animId;
        CurrentState = state;
        MarkAnimSyncDirty(); // 状态变化 = 动画事件的唯一触发源（trigger 不参与同步）
    }

    public void GetDisplayAnim(out int animId, out float normalizedTime)
    {
        animId = currentAnimId;
        normalizedTime = 0f;
        if (mainAnimator != null && mainAnimator.layerCount > 0)
        {
            // 过渡期间 GetCurrentAnimatorStateInfo 仍是旧状态；若 next 正是 currentAnimId 新状态，进度须从 next 读以保证 hash 与进度配对一致
            var st = mainAnimator.GetCurrentAnimatorStateInfo(0);
            if (mainAnimator.IsInTransition(0) &&
                mainAnimator.GetNextAnimatorStateInfo(0).fullPathHash == currentAnimId)
            {
                st = mainAnimator.GetNextAnimatorStateInfo(0);
            }
            normalizedTime = st.length > 0f ? Mathf.Repeat(st.normalizedTime, 1f) : 0f;
        }
    }

    #region//武器显示（持有哪把武器由实体配表决定，显示位置由动画状态决定）
    private int weaponId = -1;
    private WeaponDisplayType weaponDisplay;
    private WeaponPos weaponPos = WeaponPos.Hide;
    private readonly List<GameObject> weaponVisuals = new();
    private EntityModelInfo modelInfo;

    public int WeaponId => weaponId;

    // 实体固定武器：服务器与客户端视图、首页角色各在 Init 之后调一次
    public void SetWeapon(int id, WeaponDisplayType display)
    {
        weaponId = id;
        weaponDisplay = display;
        RefreshWeaponVisuals();
    }

    // 每次进入新动画状态时由 AnimEvent 调用
    public void SetWeaponPos(WeaponPos pos)
    {
        if (weaponPos == pos) return;
        weaponPos = pos;
        RefreshWeaponVisuals();
    }

    private void RefreshWeaponVisuals()
    {
        for (int i = 0; i < weaponVisuals.Count; i++)
        {
            if (weaponVisuals[i] != null) Destroy(weaponVisuals[i]);
        }
        weaponVisuals.Clear();

        if (weaponPos == WeaponPos.Hide || weaponId < 0) return;
        if (modelInfo == null)
        {
            Debug.LogError($"{gameObject.name} 缺少 EntityModelInfo，武器挂点解析不了");
            return;
        }
        if (Tool.AssetsManager == null)
        {
            Debug.LogError($"{gameObject.name} 显示武器需要 AssetsManager");
            return;
        }
        if (!Tool.AssetsManager.TryGetWeaponPrefab(weaponId, out var prefab))
        {
            Debug.LogError($"{gameObject.name} 的武器 {weaponId} 不在 AssetsManager 的武器列表中");
            return;
        }
        if (weaponPos == WeaponPos.Hand)
        {
            AttachToBone(prefab, EntityAnchor.RightHand);
            if (weaponDisplay == WeaponDisplayType.DualSword) AttachToBone(prefab, EntityAnchor.LeftHand);
            return;
        }
        // 收起：长枪挂背，刀挂右腰，双刀左右腰各一把
        switch (weaponDisplay)
        {
            case WeaponDisplayType.Spear:
                AttachToAnchor(prefab, EntityAnchor.WeaponBack);
                break;
            case WeaponDisplayType.Sword:
                AttachToAnchor(prefab, EntityAnchor.WeaponHipRight);
                break;
            case WeaponDisplayType.DualSword:
                AttachToAnchor(prefab, EntityAnchor.WeaponHipLeft);
                AttachToAnchor(prefab, EntityAnchor.WeaponHipRight);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(weaponDisplay), weaponDisplay, "未知武器显示类型");
        }
    }

    // 手上：直接作为骨骼子物体，跟骨骼旋转一起动
    private void AttachToBone(GameObject prefab, EntityAnchor anchor)
    {
        Transform bone = anchor.GetTransform(modelInfo, this).transform;
        if (bone == null)
        {
            Debug.LogError($"{gameObject.name} 的 {anchor} 锚点取不到骨骼，武器无法显示");
            return;
        }
        Transform mount = CreateMount(bone, anchor.ToString());
        mount.localPosition = Vector3.zero;
        mount.localRotation = Quaternion.identity;

        GameObject instance = Instantiate(prefab, mount);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        weaponVisuals.Add(mount.gameObject);
    }

    // 收起：挂点必须建在骨骼下（武器要跟着人物动画动）；位置与朝向取锚点算出的世界值
    private void AttachToAnchor(GameObject prefab, EntityAnchor anchor)
    {
        EntityAnchorInfo info = anchor.GetTransform(modelInfo, this);
        if (info.transform == null)
        {
            Debug.LogError($"{gameObject.name} 的 {anchor} 锚点没有关联骨骼，武器不会跟随动画");
            return;
        }
        Transform mount = CreateMount(info.transform, anchor.ToString());
        mount.position = info.position;
        mount.rotation = info.rotation;

        GameObject instance = Instantiate(prefab, mount);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        weaponVisuals.Add(mount.gameObject);
    }

    // 骨骼常带百倍量级的缩放（美术导出遗留）：挂点按骨骼世界缩放反向抵消，武器保持预制体原大小
    private Transform CreateMount(Transform bone, string label)
    {
        Transform mount = new GameObject($"{name}_WeaponMount_{label}").transform;
        mount.SetParent(bone, false);

        Vector3 scale = bone.lossyScale;
        if (Mathf.Approximately(scale.x, 0f) || Mathf.Approximately(scale.y, 0f) || Mathf.Approximately(scale.z, 0f))
        {
            Debug.LogError($"{gameObject.name} 的 {label} 挂点骨骼缩放为 0：{scale}，武器大小无法换算");
            return mount;
        }
        mount.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
        return mount;
    }
    #endregion

    #region//设置速度
    private float PlaybackSpeed => paused ? 0f : speed;
    public void SetVelocityForward(float speed)
    {
        if (data == null) return;
        data.SetVelocityForward(speed * PlaybackSpeed, VelocitySource.Animation);
    }
    public void SetVelocityHorizontal(Vector2 speed)
    {
        if (data == null) return;
        data.SetVelocityHorizontal(speed * PlaybackSpeed, VelocitySource.Animation);
    }
    public void SetVelocityVertical(float speed)
    {
        if (data == null) return;
        data.SetVelocityVertical(speed, VelocitySource.Animation);
    }
    #endregion
    #region//速度控制
    private float speed=1;
    private bool paused=false;

    public float MoveSpeedScale => speed;
    public bool Paused => paused;

    public void SetPaused(bool paused)
    {
        if (this.paused == paused) return;
        this.paused = paused;
        UpdateSpeed();
        MarkAnimSyncDirty(); // 播放速度变化也要同步（否则被强控时客户端动画照播）
    }

    public void SetMoveSpeedScale(float scale)
    {
        if (Mathf.Approximately(speed, scale)) return;
        speed = scale;
        UpdateSpeed();
        MarkAnimSyncDirty();
    }
    private void UpdateSpeed()
    {
        float target = PlaybackSpeed; // 与声明速度用的同一份"动画播放速度"，两处不会走散
        foreach (var animator in animators)
            animator.speed = target;
    }
    #endregion

    #region 骨骼相关
    private Dictionary<HumanBodyBones, Transform> boneCache = new();
    public Transform GetBoneTransform(HumanBodyBones humanBoneId)
    {
        if (boneCache.TryGetValue(humanBoneId, out Transform boneTransform)) return boneTransform;

        boneTransform = mainAnimator != null ? mainAnimator.GetBoneTransform(humanBoneId) : null;
        // 简化绑定常缺骨骼（如 Chest 未映射）：沿躯干链回退，避免锚点取到空引用
        if (boneTransform == null && mainAnimator != null)
            boneTransform = mainAnimator.GetBoneTransform(HumanBodyBones.Spine)
                ?? mainAnimator.GetBoneTransform(HumanBodyBones.Hips);
        if (boneTransform == null) boneTransform = mainAnimator != null ? mainAnimator.transform : transform;

        boneCache[humanBoneId] = boneTransform;
        return boneTransform;
    }
    #endregion

    #region//参数打包（持久参数随动画事件同步；trigger 不同步）
    private AnimParamPack paramPack = AnimParamPack.Default; // 持久参数（int/bool），所有设置必须经过这里

    public bool AnimSyncDirty { get; private set; }
    public void MarkAnimSyncDirty() => AnimSyncDirty = true;
    public void ClearAnimSyncDirty() => AnimSyncDirty = false;

    public AnimParamPack GetParamPack()
    {
        return paramPack;
    }

    public void ApplyParamPack(in AnimParamPack pack)
    {
        if (animators == null || animators.Count == 0) return;
        paramPack = pack; // 记录持久值（客户端：GetParamPack 也取得到）；不置脏——客户端不做同步
        PushParamsToAnimators();
    }

    private void SetIntParam(ref int field, int value, string key)
    {
        if (field == value) return;
        field = value;
        SetIntAll(key, value);
        MarkAnimSyncDirty();
    }

    private void SetBoolParam(ref bool field, bool value, string key)
    {
        if (field == value) return;
        field = value;
        SetBoolAll(key, value);
        MarkAnimSyncDirty();
    }

    private void SetIntAll(string key, int value)
    {
        foreach (var animator in animators)
            animator.SetInteger(key, value);
    }

    private void SetBoolAll(string key, bool value)
    {
        foreach (var animator in animators)
            animator.SetBool(key, value);
    }

    private void SetTriggerAll(string key)
    {
        foreach (var animator in animators)
            animator.SetTrigger(key);
    }
    #endregion

    #region//动画控制
    public void DoSpawn()
    {
        SetTriggerAll(key_spawn);
    }
    public void DoJump()
    {
        SetTriggerAll(key_jump);
    }
    public void InAir(bool inAir)
    {
        SetBoolParam(ref paramPack.inAir, inAir, key_inAir); // 每帧都会被调用，值未变不置脏
    }
    public void Move(bool moving)
    {
        SetBoolParam(ref paramPack.moving, moving, key_moving);
    }
    public void DoSlide()
    {
        SetBoolParam(ref paramPack.slide, true, key_slide);
    }
    public void EndSlide()
    {
        SetBoolParam(ref paramPack.slide, false, key_slide);
    }
    public void DoAttack(AttackType attack)
    {
        SetIntParam(ref paramPack.attackId, (int)attack, key_attackId);
        SetTriggerAll(key_doAttack);
    }
    public void DoHit(bool longHit)
    {
        SetIntParam(ref paramPack.hit, longHit?1:0, key_hit); // 长短受击选择：值未变时不重写，靠 trigger 负责重播
        SetTriggerAll(key_doHit);
    }
    #endregion
}
