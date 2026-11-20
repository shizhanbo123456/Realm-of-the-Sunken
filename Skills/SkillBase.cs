using System;
using System.Collections.Generic;
using Ros.Skill.Utils;
using Ros.Transport;
using UnityEngine;

using Ros.Config;
namespace Ros.Skill
{
    // 键值会被 effect.csv 第 11 列以整数引用，新类别只能追加在末尾
    public enum SkillVfxKind { None, Bullet, RangeMagic, MagicCircle, Shield, Buff, Weapon, Slash, Projection }

    public abstract class SkillBase
    {
        public abstract int Id { get; }

        // 射程读 skill.csv 第 14 列：既是技能能影响到的距离，也是索敌半径与 AI 的作战距离；0 = 只影响自身
        public float CastRange => SkillConfig.Get(Id).castRange;
        // 索敌口径读 skill.csv 第 13 列：夹角 <=0.1 = 不索敌，>179.9 = 不限方向；半径一律取射程
        protected float TargetSector => SkillConfig.Get(Id).targetSector;
        protected float TargetRadius => CastRange;
        // 第 17 列：释放瞬间是否自动转向射程内最近的敌人
        private bool AutoTarget => SkillConfig.Get(Id).autoTarget ?? false;
        // 攻击动画读 skill.csv 第 12 列，留空 = 无动画（按下立刻施放）
        protected bool HasCastAnim => SkillConfig.Get(Id).attackAnim.HasValue;
        // 只在 HasCastAnim 为真时读
        protected EntityAnim.AttackType CastAnim => (EntityAnim.AttackType)SkillConfig.Get(Id).attackAnim.Value;

        // 武器不随技能变化：一律取实体固定武器（服务器与客户端各从自己的实体拿）
        protected static int WeaponOf(EntityData entity) => entity.weaponId;

        // 客户端表现视图没有 EntityData，按包里施法者 id 取视图上的固定武器
        protected static int DisplayWeapon(SCSkillDisplayContext context)
        {
            if (Tool.ClientLogicManager == null) return -1;
            var view = Tool.ClientLogicManager.EntityPlayers.GetView(context.caster.id);
            return view != null && view.anim != null ? view.anim.WeaponId : -1;
        }

        // 服务器唯一结算入口：直接拿实体干活，不经过任何上下文
        protected abstract void OnCast(EntityData caster);

        // 客户端表现入口：按波次 id 分派，只做表现
        public virtual void PlayWave(SCSkillDisplayContext context) { }

        // 施放入口：按下时先摇攻击动画，动画到攻击帧时才结算（此时索敌/起点基于位移后的实时位置）
        public void BeginCast(EntityData entity, int skillId)
        {
            BroadcastSpecial(skillId); // 每次施放请求只播报一次，不走 CastNow（多段攻击帧会重复）
            if (AutoTarget) FaceNearestEnemy(entity); // 转向必须在算落点之前：不索敌的技能靠朝向决定弹道
            if (!HasCastAnim || entity.anim == null)
            {
                Cast(entity);
                return;
            }
            entity.anim.onAttack = _ => Cast(entity); // 不置空回调：多段攻击帧逐次结算技能
            entity.anim.DoAttack(CastAnim);
        }

        // 每次攻击结算前先记录蓄力值并立即清零：是否乘算只看技能表「启用蓄力」
        private void Cast(EntityData entity)
        {
            entity.anim?.TakeCharge();
            OnCast(entity);
        }

        // 自动索敌：转向射程内最近的敌人（不限方向），转向后落点照常按新朝向算；范围内没敌人就保持原朝向
        private void FaceNearestEnemy(EntityData caster)
        {
            if (TargetRadius <= 0f) return; // 射程 0 = 只影响自身，没有可供瞄准的方向
            EntityData target = BattleManager.EntityContainer.GetNearestEnemy(caster, TargetRadius);
            if (target != null) caster.FaceTowards(target.transform.position);
        }

        // 发一个波次包：头由基类填，payload 由技能自己按读端同样的顺序写
        protected void SendWave(EntityData caster, int wave, Action<SCSkillDisplayContext> write)
        {
            if (caster == null) throw new ArgumentNullException(nameof(caster), "发波次包需要施法者实体");
            if (wave < 0 || wave > byte.MaxValue) throw new ArgumentOutOfRangeException(nameof(wave), wave, "波次 id 超出 1 字节");
            var context = new SCSkillDisplayContext();
            context.SkillId = (byte)Id;
            context.SkillWave = (byte)wave;
            context.caster = new SkillTarget(caster);
            write(context);
            if (Tool.NetworkManager != null) Tool.NetworkManager.SendSkillCast(context);
            else PlayWave(context); // 单机测试场景没有网络，直接本地播一遍表现
        }

        // 配表第 18 列：特殊技能施放时向全客户端播报，客户端据此弹战斗界面的重要事件 banner
        private void BroadcastSpecial(int skillId)
        {
            if (!BattleManager.AtServer || Tool.NetworkManager == null) return;
            if (SkillConfig.Get(skillId).broadcast != true) return;
            Tool.NetworkManager.SendBattleEvent(new SCBattleEvent
            {
                type = SCBattleEvent.Type.SkillBroadcast,
                textId = skillId,
            });
        }

        #region 框架工具（起点 / 落点 / 攻击结算）
        private static readonly HashSet<int> s_weaponHitWarned = new();
        private static readonly HashSet<int> s_weaponVisualWarned = new();

        // 近战判定窗（秒）：隐形子弹的存活时长，覆盖整段挥刀动作；客户端近战打击感按同一个值开窗
        private static float MeleeBulletLife => GlobalVariable.F(global_variablesEnum.melee_bullet_life);

        // 起点 = 实体骨骼锚点 + 实体朝向下的固定偏移
        protected static Vector3 OriginPos(EntityData entity, EntityAnchor anchor, Vector3 offset)
        {
            Vector3 p = anchor.GetTransform(entity.ModelInfo, entity.anim).position;
            return p + entity.transform.rotation * offset;
        }

        // 本次施法的索敌目标：服务器结算时解析一次，客户端用包里下发的点位，不复算
        protected EntityData ResolveTarget(EntityData entity)
            => TargetSelect.GetNearestEnemy(entity, TargetRadius, TargetSector);

        // 落点口径（见《03-技能与Buff总表》三章）：不索敌 = 朝面向、水平距离取射程、与起点同高；
        // 索敌 = 朝目标体中心、水平距离仍取射程、高度取体中心。
        // 距离一律从施法者自身位置起算，终点到身体的距离恒 = 射程，不受发射锚点在体心/身前/手上影响
        protected Vector3 Aim(EntityData caster, Vector3 origin)
        {
            if (CastRange <= 0f)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 需要瞄准点，但第 14 列没有配射程");
            Vector3 pos = caster.transform.position;
            Vector3 forward = caster.transform.forward;
            EntityData target = ResolveTarget(caster);
            if (target == null)
                return new Vector3(pos.x + forward.x * CastRange, origin.y, pos.z + forward.z * CastRange);
            Vector3 body = TargetSelect.BodyPos(target);
            Vector3 flat = body - pos;
            flat.y = 0f;
            Vector3 dir = flat.sqrMagnitude > 0.0001f ? flat.normalized : forward;
            return new Vector3(pos.x + dir.x * CastRange, body.y, pos.z + dir.z * CastRange);
        }

        // 按配表半径收集场内敌对目标（半径 >9999 = 不限距离）；center 由技能决定
        protected void CollectEnemies(EntityData caster, Vector3 center)
            => TargetSelect.SetEnemiesInRangeToBuffer(center, TargetRadius, caster.type.category);

        // 按配表半径筛选指定集合（半径 >9999 = 不限距离），如水晶雕像 / 守护雕像 / 珊瑚
        protected void CollectInRange(EntityData caster, IEnumerable<EntityData> bucket)
            => TargetSelect.SetEntitiesInRangeToBuffer(bucket, caster.transform.position, TargetRadius);

        // 按 id 找实体（服务器权威容器；客户端查不到返回 null）
        protected static EntityData FindEntity(ushort id) => BattleManager.GetEntity(id);

        // 击退力度默认读 skill.csv，个别技能可显式覆盖；伤害范围由命中球决定，不在攻击数据里
        // 破霸体读第 16 列，伤害倍率读第 11 列：代码不再传这两项
        protected AttackData BuildAttack(EntityData entity,
            Action<EntityEffectController> addEffect = null,
            Action<EntityData> onHit = null, float? knockback = null)
        {
            var config = SkillConfig.Get(Id);
            float power = knockback ?? config.knockback ?? 0f;
            if (config.chargeEnabled == true) power *= ChargeScale(entity);
            return AttackData.Create(entity, config.breakEndure ?? false,
                addEffectEvent: addEffect, onHit: onHit, knockbackPower: power);
        }

        // 蓄力倍率 = 本次攻击记录到的蓄力值 + 1，记录点在 Cast 入口
        private float ChargeScale(EntityData entity)
        {
            if (entity.anim == null)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 启用了蓄力，但施法者没有 EntityAnim，蓄力值取不到");
            return entity.anim.LastCharge + 1f;
        }

        // 技能附带的 Buff 全部读 skill.csv，按 index 取用；越界属配表错配，直接抛
        protected BuffEntry ConfigBuff(int index)
        {
            var config = SkillConfig.Get(Id);
            if (index < 0 || index >= config.BuffCount())
                throw new FormatException($"[配表] skill.csv 技能 {Id} 没有第 {index} 个 Buff（共 {config.BuffCount()} 个）");
            return config.BuffAt(index);
        }

        protected Action<EntityEffectController> ConfigHitBuff(EntityData caster, int index)
        {
            var buff = ConfigBuff(index);
            return effect => effect.AddEffect((EffectType)buff.type, buff.level, buff.duration, caster);
        }

        protected void ApplyConfigBuff(EntityData caster, EntityData target, int index)
        {
            if (target == null || target.effectController == null) return;
            var buff = ConfigBuff(index);
            target.effectController.AddEffect((EffectType)buff.type, buff.level, buff.duration, caster);
        }

        // 第 9 列：有特效的技能 = 各特效的缩放倍率（空 = 原大小），无特效近战 = 判定半径
        private List<float> SizedConfig => SkillConfig.Get(Id).bulletSizes;

        // 取第 index 个特效的缩放倍率；没配 = 原大小，配了但越界属错配，直接抛
        protected float VfxScale(int index = 0)
        {
            var sizes = SizedConfig;
            if (sizes.Count == 0) return 1f;
            if (index < 0 || index >= sizes.Count)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 只配了 {sizes.Count} 个尺寸，取第 {index} 个越界");
            return sizes[index];
        }

        // 无特效近战的判定半径：只认配表，缺配置立刻抛
        protected float MeleeRadius
        {
            get
            {
                var sizes = SizedConfig;
                if (sizes.Count == 0)
                    throw new FormatException($"[配表] skill.csv 技能 {Id} 是无特效近战，第 9 列必须配判定半径");
                return sizes[0];
            }
        }

        // 第 10 列：同一目标最短再伤害间隔（秒）；留空在解析阶段已填 100 = 只结算一次
        protected float DamageInterval => SkillConfig.Get(Id).damageInterval ?? 100f;

        // 第 11 列指定项；空 = 全 1，越界属配表错配直接抛（与段倍率共用同一列）
        protected float ConfigDamageRate(int index = 0)
        {
            var rates = SkillConfig.Get(Id).damageRates;
            if (rates.Count == 0) return 1f;
            if (index < 0 || index >= rates.Count)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 只配了 {rates.Count} 个伤害倍率，取第 {index} 个越界");
            return rates[index];
        }

        // 第 11 列：伤害倍率。空 = 全 1（不摊分）；只有 1 项 = 所有段共用；多项 = 按段序取。
        // 表里填的是一次施放的总量，按发数均摊到每颗子弹：一个子弹结算多次伤害也只算一发
        private float[] SegmentRates(int segments, int bullets)
        {
            var rates = SkillConfig.Get(Id).damageRates;
            if (rates.Count == 0) return null;
            if (rates.Count == 1)
            {
                var same = new float[segments];
                for (int i = 0; i < segments; i++) same[i] = rates[0] / bullets;
                return same;
            }
            if (rates.Count < segments)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 伤害倍率只有 {rates.Count} 项，不够 {segments} 段用");
            var arr = new float[segments];
            for (int i = 0; i < segments; i++) arr[i] = rates[i] / bullets;
            return arr;
        }

        // 组装子弹命中判定参数：命中球与存活时长取特效预制体，倍率/间隔取配表
        // bullets = 本次施放共发射几颗子弹（含多波累加、含分裂），单发技能不用传
        protected BulletHitSpec HitSpec(SkillVfxKind kind, int vfxIndex, float scale, int bullets = 1)
        {
            if (bullets <= 0) throw new ArgumentOutOfRangeException(nameof(bullets), bullets, "发数必须 > 0");
            var hits = VfxHitSource.GetHits(kind, vfxIndex);
            return new BulletHitSpec(hits, SegmentRates(hits.Length, bullets), scale, DamageInterval,
                VfxHitSource.GetLifeTime(kind, vfxIndex), $"{kind}[{vfxIndex}]");
        }

        // 整段共用同一倍率：二次爆炸这类独立效果只从第 11 列取指定项，与命中球段数无关
        protected BulletHitSpec HitSpec(SkillVfxKind kind, int vfxIndex, float scale, float rate, int bullets = 1)
        {
            if (bullets <= 0) throw new ArgumentOutOfRangeException(nameof(bullets), bullets, "发数必须 > 0");
            var hits = VfxHitSource.GetHits(kind, vfxIndex);
            var rates = new float[hits.Length];
            for (int i = 0; i < rates.Length; i++) rates[i] = rate / bullets;
            return new BulletHitSpec(hits, rates, scale, DamageInterval,
                VfxHitSource.GetLifeTime(kind, vfxIndex), $"{kind}[{vfxIndex}]");
        }

        // 武器近战：发一颗隐形子弹贴着武器实例走完整段挥刀，判定球中心与半径取武器预制体的 BulletVfxInfo
        protected void StrikeWeapon(EntityData entity, AttackData attack)
        {
            int weapon = WeaponOf(entity);
            var hits = VfxHitSource.GetHits(SkillVfxKind.Weapon, weapon);
            if (hits.Length == 0)
            {
                if (s_weaponHitWarned.Add(weapon))
                    Debug.LogError($"[武器] 武器 {weapon} 没有命中球，普攻不造成伤害（每个武器只报一次）");
                return;
            }
            // 判定球跟着武器实例走：取不到实例（模型未挂武器 / 当前片段隐藏武器）就不发弹，避免球心落到原点
            if (!BulletTrajectory.TryGetWeaponPose(entity.id, out _, out _, out _))
            {
                if (s_weaponVisualWarned.Add(weapon))
                    Debug.LogError($"[武器] 武器 {weapon} 的实例不在场上（当前动画隐藏武器或模型没挂上），近战不造成伤害");
                return;
            }
            float scale = VfxScale();
            float rate = ConfigDamageRate();
            var spheres = new BulletVfxInfo.HitInfo[hits.Length];
            var rates = new float[hits.Length];
            for (int i = 0; i < hits.Length; i++)
            {
                // 只取中心与半径：判定窗由挥刀时长决定，资产里配的 start/end 不参与
                spheres[i] = new BulletVfxInfo.HitInfo { center = hits[i].center, radius = hits[i].radius };
                rates[i] = rate;
            }
            var spec = new BulletHitSpec(spheres, rates, scale, DamageInterval, 0f, $"近战[武器{weapon}]");
            Tool.BattleManager.ShootBullet(entity, attack, new SkillTrajectoryWeapon(entity.id), MeleeBulletLife, spec);
        }
        #endregion
    }
}
