using Ros.Config;
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
    private const string key_combo = "Combo";
    private const string key_breakCombo = "BreakCombo";
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
        Combo=0,//普通攻击
        Attack_Hand=10,//空手挥打
        Attack_Weapon=11,//武器挥打
        Attack_AirDrop =20,//跳起来抱摔
        Attack_AirFront=21,//跳起来投掷
        Kick=30,//无特效
        Scream=31,//吼叫
        Mega_Short=40,//法术动作(短)
        Mega_Middle=41,//法术动作(中)
        Mega_Long =42,//法术动作(长)
        Weapon_A =50,//武器动作A
        Weapon_B=51,//武器动作B
        Weapon_C =52,//武器动作C
        Weapon_D =53,//武器动作D
    }
    public enum WeaponPos
    {
        Hide,   // 不显示
        Show,   // 收起：长枪背在背上，刀收在腰间
        Hand,   // 拿在手上
        DontSet//不设置
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

    
    public Action<AttackType> onAttack;//动画中的攻击事件回调
    public Action<AnimState, AnimState> OnStateChange;

    private List<Animator> animators;
    private Animator mainAnimator;

    public Animator MainAnimator => mainAnimator;


    public void Init(EntityData data, EntityType type)
    {
        this.data = data;

        // 参数包复位：重复 Init（如视图重建）时清掉上一轮残留的持久参数
        paramPack = AnimParamPack.Default;
        chargeValue = 0f;
        lastCharge = 0f;
        ApplyBaseAnimSpeed(type); // 基础播放倍率取自配表，服务器与客户端都读同一行

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
        EnsureWeaponSocket();
        CacheAnchorRestOffsets(); // 必须早于参数推送：此时还是绑定姿势
        for (int i = 0; i < animators.Count; i++)
        {
            Animator animator = animators[i];
            // 动画机由预制体自带；漏配时状态机为空、动画不播且不报错，故显式拦一条
            if (animator.runtimeAnimatorController == null)
                Debug.LogError($"{gameObject.name} 的 Animator「{animator.name}」没有配 runtimeAnimatorController：动画不会播放");
            var behaviours = animator.GetBehaviours<AnimEvent>();
            foreach (var behaviour in behaviours) behaviour.Init(this, data,i==0);
        }
        // 把复位后的参数推给状态机：保证"paramPack 与 Animator 参数一致"这个前提（值比较的脏标记依赖它）
        PushParamsToAnimators();
        ApplyPlaybackSpeed(); // 基础倍率已就位，落到动画机上
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
        ApplyPlaybackSpeed(); // 换阶段 = 换基础倍率（移动/攻击各一套）
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
    private readonly List<Transform> weaponInstances = new(); // 武器预制体实例：近战判定球按它的实际位姿摆放
    private EntityModelInfo modelInfo;

    public int WeaponId => weaponId;

    // 第一把武器实例（含 WeaponFix 标定旋转）；收起/无武器时为 null
    public Transform WeaponTransform => weaponInstances.Count > 0 ? weaponInstances[0] : null;

    public bool WeaponInHand => weaponPos == WeaponPos.Hand;

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
        if (pos == WeaponPos.DontSet) return; // 该片段不参与武器显示，保持上一个状态
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
        weaponInstances.Clear();

        if (weaponPos == WeaponPos.Hide || weaponId < 0) return;
        if (modelInfo == null)
        {
            Debug.LogError($"{gameObject.name} 缺少 EntityModelInfo，武器挂点解析不了");
            return;
        }
        // 武器预制体：服务器取 AssetsManager 的直接引用，客户端取 ResourceManager（进游戏时已预加载）
        GameObject prefab = null;
        bool found = Tool.IsServer
            ? Tool.AssetsManager != null && Tool.AssetsManager.TryGetWeaponPrefab(weaponId, out prefab)
            : Tool.ResourceManager != null && Tool.ResourceManager.TryGetWeaponPrefab(weaponId, out prefab);
        if (!found)
        {
            Debug.LogError($"{gameObject.name} 的武器 {weaponId} 不在 {(Tool.IsServer ? "AssetsManager" : "ResourceManager")} 的武器列表中");
            return;
        }
        if (weaponPos == WeaponPos.Hand)
        {
            AttachToBone(prefab, EntityAnchor.RightHand);
            if (weaponDisplay == WeaponDisplayType.DualSword) AttachToBone(prefab, EntityAnchor.LeftHand);
        }
        // 收起：长枪挂背，刀挂右腰，双刀左右腰各一把
        else
        {
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
        RefreshWeaponTrail();
    }

    private const int WeaponTrailChildIndex = 1; // 刀光物体固定在武器预制体根的第 2 个子物体上

    // 刀光只跟手上走：手部开、腰背关；缺子物体或 TrailRenderer 只报错，不做兜底搜索
    private void RefreshWeaponTrail()
    {
        bool enable = weaponPos == WeaponPos.Hand;
        for (int i = 0; i < weaponInstances.Count; i++)
        {
            Transform root = weaponInstances[i];
            if (root == null) continue;
            if (root.childCount <= WeaponTrailChildIndex)
            {
                Debug.LogError($"{gameObject.name} 的武器 {weaponId} 缺少第 {WeaponTrailChildIndex} 个子物体：刀光不生效，需调整预制体层级");
                return;
            }
            TrailRenderer trail = root.GetChild(WeaponTrailChildIndex).GetComponent<TrailRenderer>();
            if (trail == null)
            {
                Debug.LogError($"{gameObject.name} 的武器 {weaponId} 第 {WeaponTrailChildIndex} 个子物体没有 TrailRenderer：刀光不生效，需调整预制体层级");
                return;
            }
            trail.enabled = enable;
        }
    }

    // 手上：直接作为骨骼子物体，跟骨骼旋转一起动
    private void AttachToBone(GameObject prefab, EntityAnchor anchor)
    {
        Transform socket = anchor == EntityAnchor.RightHand ? weaponSocketR : anchor == EntityAnchor.LeftHand ? weaponSocketL : null;
        bool useBone = socket != null && animData != null && animData.useWeaponBone;
        Transform bone = useBone ? socket : anchor.GetTransform(modelInfo, this).transform;
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
        instance.transform.localRotation = WeaponFix(anchor);
        weaponInstances.Add(instance.transform);
        weaponVisuals.Add(mount.gameObject);
    }

    // 手部标定：右手的 Y 把矛身转到骨骼 X 轴、Z 自转 180° 翻刃口；左手是右手的镜像
    private static readonly Vector3 WeaponHandFixEuler = new Vector3(0f, 90f, 180f);
    private static readonly Vector3 WeaponLeftHandFixEuler = new Vector3(0f, -90f, 180f);

    private static Quaternion WeaponFix(EntityAnchor anchor)
    {
        if (anchor == EntityAnchor.RightHand) return Quaternion.Euler(WeaponHandFixEuler);
        if (anchor == EntityAnchor.LeftHand) return Quaternion.Euler(WeaponLeftHandFixEuler);
        return Quaternion.identity;
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
        // 位置和朝向都换算到骨骼本地：偏移必须跟着骨盆转，否则胯一转武器还停在实体的左右两侧
        Quaternion rest = anchor == EntityAnchor.WeaponBack ? chestRestOffset : hipsRestOffset;
        Vector3 localOffset = Quaternion.Inverse(transform.rotation) * (info.position - info.transform.position);
        mount.position = info.transform.position + (info.transform.rotation * rest) * localOffset;
        Quaternion tilt = Quaternion.Inverse(transform.rotation) * info.rotation;
        mount.localRotation = rest * tilt;

        GameObject instance = Instantiate(prefab, mount);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = WeaponFix(anchor);
        weaponInstances.Add(instance.transform);
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
    // 声明速度与动画播放同源：两者乘同一个播放倍率，位移与动画节奏不会走散
    private float PlaybackSpeed => paused ? 0f : PlaybackScale;
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
    private const float DefaultBaseSpeed = 1f; // 配表该列留空 = 不缩放
    private float baseMoveSpeed = DefaultBaseSpeed;   // entity 表「基础移动动画速度」
    private float baseAttackSpeed = DefaultBaseSpeed; // entity 表「基础攻击动画速度」
    private float buffMoveScale = 1f;  // Buff 移速倍率：只作用于移动阶段
    private bool paused = false;

    // 下发给客户端的就是 Buff 移速倍率（阶段基础倍率两端各自按配表算，不占协议字段）
    public float MoveSpeedScale => buffMoveScale;
    public bool Paused => paused;

    public void SetPaused(bool paused)
    {
        if (this.paused == paused) return;
        this.paused = paused;
        ApplyPlaybackSpeed();
        MarkAnimSyncDirty(); // 播放速度变化也要同步（否则被强控时客户端动画照播）
    }

    // 客户端本地卡肉：只冻结表现播放，不进同步（服务器永远不置这个状态）
    private float hitStopUntil;

    public bool InHitStop => Time.unscaledTime < hitStopUntil;

    public void BeginHitStop(float seconds)
    {
        float until = Time.unscaledTime + seconds;
        if (until <= hitStopUntil) return;
        hitStopUntil = until;
        ApplyPlaybackSpeed();
    }

    // Buff 移速倍率：只改移动阶段，攻击/受击/起身不受影响
    public void SetMoveSpeedScale(float scale)
    {
        if (Mathf.Approximately(buffMoveScale, scale)) return;
        buffMoveScale = scale;
        ApplyPlaybackSpeed();
        MarkAnimSyncDirty();
    }

    // 配表基础倍率：Init 时按实体类型读，两列留空就是 1 倍
    private void ApplyBaseAnimSpeed(EntityType type)
    {
        entity config = EntityConfig.GetByCategory(type);
        baseMoveSpeed = config.animMovementSpeed ?? DefaultBaseSpeed;
        baseAttackSpeed = config.animAttackSpeed ?? DefaultBaseSpeed;
    }

    // 阶段基础倍率：移动/攻击各取配表列，起身与受击不缩放
    private float PhaseBaseSpeed => currentState switch
    {
        AnimState.Motion => baseMoveSpeed,
        AnimState.Attack => baseAttackSpeed,
        _ => DefaultBaseSpeed,
    };

    // 两端同一套公式：换阶段（NotifyStateEnter）时重算
    private float PlaybackScale => PhaseBaseSpeed * (currentState == AnimState.Motion ? buffMoveScale : 1f);

    private void ApplyPlaybackSpeed()
    {
        if (animators == null || animators.Count == 0) return;
        float target = paused || InHitStop ? 0f : PlaybackScale;
        foreach (var animator in animators)
            animator.speed = target;
    }
    #endregion

    #region//蓄力
    // 蓄力值：AnimEvent 按片段速率逐帧累加，攻击结算时记录并清零
    private float chargeValue;
    private float lastCharge;

    public float LastCharge => lastCharge;

    public void AddCharge(float delta)
    {
        if (Mathf.Approximately(delta, 0f)) return;
        chargeValue = Mathf.Clamp(chargeValue + delta, 0f, GlobalVariable.F(global_variablesEnum.charge_max));
    }

    // 攻击结算入口调用：先记录本次蓄力值，再立即清零（不论技能是否启用蓄力）
    public float TakeCharge()
    {
        lastCharge = chargeValue;
        chargeValue = 0f;
        return lastCharge;
    }
    #endregion

    #region 骨骼相关
    // 手上挂点：骨架本身没有武器槽，运行时在左右手骨骼下各建一个，位置由各自的偏移决定
    private Transform weaponSocketR;
    private Transform weaponSocketL;
    private Vector3 handInvScaleR = Vector3.one;
    private Vector3 handInvScaleL = Vector3.one;

    private const string socketNameR = "WeaponSocket_R";
    private const string socketNameL = "WeaponSocket_L";

    private void EnsureWeaponSocket()
    {
        if (animData == null || !animData.useWeaponBone) return;
        if (mainAnimator == null || mainAnimator.avatar == null || !mainAnimator.avatar.isHuman) return;
        weaponSocketR = CreateSocket(HumanBodyBones.RightHand, socketNameR, out handInvScaleR);
        weaponSocketL = CreateSocket(HumanBodyBones.LeftHand, socketNameL, out handInvScaleL);
    }

    private Transform CreateSocket(HumanBodyBones boneId, string name, out Vector3 invScale)
    {
        invScale = Vector3.one;
        Transform hand = mainAnimator.GetBoneTransform(boneId);
        if (hand == null)
        {
            Debug.LogError($"{gameObject.name} 取不到 {boneId} 骨骼，{name} 建不出来");
            return null;
        }
        Vector3 s = hand.lossyScale; // 骨骼本地单位换算成米：手的缩放是 100 时，本地 0.001 = 0.1 米
        invScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
        Transform socket = hand.Find(name);
        if (socket == null)
        {
            socket = new GameObject(name).transform;
            socket.SetParent(hand, false);
        }
        return socket;
    }

    private void LateUpdate()
    {
        // 卡肉到期要有人把播放速度放回去，否则会一直冻在 0
        if (hitStopUntil > 0f && Time.unscaledTime >= hitStopUntil)
        {
            hitStopUntil = 0f;
            ApplyPlaybackSpeed();
        }

        // 逐帧写，方便运行时调偏移
        if (weaponSocketR != null) weaponSocketR.localPosition = Vector3.Scale(animData.weaponHandLocalOffset, handInvScaleR);
        if (weaponSocketL != null) weaponSocketL.localPosition = Vector3.Scale(animData.weaponLeftHandLocalOffset, handInvScaleL);
    }

    // 绑定姿势下骨骼相对实体的朝向：把挂点倾斜换算成骨骼本地常量，姿势变化时不再漂
    private Quaternion hipsRestOffset = Quaternion.identity;
    private Quaternion chestRestOffset = Quaternion.identity;

    private void CacheAnchorRestOffsets()
    {
        if (mainAnimator == null) return;
        // 走同一套 GetBoneTransform：简化绑定缺 Chest 时会回退，跟 EntityAnchor 取的骨骼保持一致
        Transform hips = GetBoneTransform(HumanBodyBones.Hips);
        if (hips != null) hipsRestOffset = Quaternion.Inverse(hips.rotation) * transform.rotation;
        Transform chest = GetBoneTransform(HumanBodyBones.Chest);
        if (chest != null) chestRestOffset = Quaternion.Inverse(chest.rotation) * transform.rotation;
    }

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
    private const float JumpAirGraceSeconds = 0.4f; // 起跳后强制算空中的时长
    private float lastJumpTime = float.NegativeInfinity;

    public void MarkJumpTime() => lastJumpTime = Time.time;
    public bool InJumpAirGrace => Time.time - lastJumpTime < JumpAirGraceSeconds;

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

    // 当前动画状态是否开连招窗口：由 AnimEvent 在每次状态进入时写入
    public bool AllowCombo { get; private set; }
    public void SetAllowCombo(bool value) => AllowCombo = value;
    public void DoCombo() => SetTriggerAll(key_combo);

    // 按下移动键打断当前攻击段：能否打断只看 Forward 是否被屏蔽（普攻连招不屏蔽 Forward）
    public void BreakCombo()
    {
        if (currentState != AnimState.Attack) return;
        if (data == null || data.InputBlocked(InputBlockOp.Forward)) return;
        SetTriggerAll(key_breakCombo);
    }
    public void DoHit(bool longHit)
    {
        SetIntParam(ref paramPack.hit, longHit?1:0, key_hit); // 长短受击选择：值未变时不重写，靠 trigger 负责重播
        SetTriggerAll(key_doHit);
    }
    #endregion
}
