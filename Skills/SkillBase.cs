using System;
using System.Collections.Generic;
using Ros.Skill.Utils;
using Ros.Transport;
using UnityEngine;

namespace Ros.Skill
{
    // 键值会被 effect.csv 第 11 列以整数引用，新类别只能追加在末尾
    public enum SkillVfxKind { None, Bullet, RangeMagic, MagicCircle, Shield, Buff, Weapon, Slash, Projection }

    public abstract class SkillBase
    {
        public abstract int Id { get; }

        // 射程读 skill.csv 第 14 列：既是技能能影响到的距离，也是索敌半径与 AI 的作战距离；0 = 只影响自身
        public float CastRange => ConfigManager.SkillConfig.Get(Id).castRange;
        // 索敌口径读 skill.csv 第 13 列：夹角 <=0.1 = 不索敌，>179.9 = 不限方向；半径一律取射程
        protected float TargetSector => ConfigManager.SkillConfig.Get(Id).targetSector;
        protected float TargetRadius => CastRange;
        // 第 17 列：释放瞬间是否自动转向射程内最近的敌人
        private bool AutoTarget => ConfigManager.SkillConfig.Get(Id).autoTarget;
        // 攻击动画读 skill.csv 第 12 列，-1 = 无动画（按下立刻施放）
        protected bool HasCastAnim => (int)CastAnim != -1;
        protected EntityAnim.AttackType CastAnim => (EntityAnim.AttackType)ConfigManager.SkillConfig.Get(Id).attackAnim;

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
                OnCast(entity);
                return;
            }
            entity.anim.onAttack = _ => OnCast(entity); // 不置空回调：多段攻击帧逐次结算技能
            entity.anim.DoAttack(CastAnim);
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
            if (!ConfigManager.SkillConfig.Get(skillId).broadcast) return;
            Tool.NetworkManager.SendBattleEvent(new SCBattleEvent
            {
                type = SCBattleEvent.Type.SkillBroadcast,
                textId = skillId,
            });
        }

        #region 框架工具（起点 / 落点 / 攻击结算）
        private static readonly EntityData[] s_hitBuffer = new EntityData[50];

        protected static EntityAnchorInfo HandPos(EntityData entity, bool leftHand = false)
        {
            if (entity == null) return default;
            return (leftHand ? EntityAnchor.LeftHand : EntityAnchor.RightHand)
                .GetTransform(entity.ModelInfo, entity.anim);
        }

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
            var config = ConfigManager.SkillConfig.Get(Id);
            return AttackData.Create(entity, config.breakEndure,
                addEffectEvent: addEffect, onHit: onHit, knockbackPower: knockback ?? config.knockback ?? 0f);
        }

        // 技能附带的 Buff 全部读 skill.csv，按 index 取用；越界属配表错配，直接抛
        protected BuffEntry ConfigBuff(int index)
        {
            var buffs = ConfigManager.SkillConfig.Get(Id).buffs;
            if (index < 0 || index >= buffs.Count)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 没有第 {index} 个 Buff（共 {buffs.Count} 个）");
            return buffs[index];
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
        private List<float> SizedConfig => ConfigManager.SkillConfig.Get(Id).bulletSizes;

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
        protected float DamageInterval => ConfigManager.SkillConfig.Get(Id).damageInterval;

        // 第 11 列指定项；空 = 全 1，越界属配表错配直接抛（与段倍率共用同一列）
        protected float ConfigDamageRate(int index = 0)
        {
            var rates = ConfigManager.SkillConfig.Get(Id).damageRates;
            if (rates.Count == 0) return 1f;
            if (index < 0 || index >= rates.Count)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 只配了 {rates.Count} 个伤害倍率，取第 {index} 个越界");
            return rates[index];
        }

        // 第 11 列：伤害倍率。空 = 全 1；只有 1 项 = 所有段共用；多项 = 按段序取
        private float[] SegmentRates(int segments)
        {
            var rates = ConfigManager.SkillConfig.Get(Id).damageRates;
            if (rates.Count == 0) return null;
            if (rates.Count == 1)
            {
                var same = new float[segments];
                for (int i = 0; i < segments; i++) same[i] = rates[0];
                return same;
            }
            if (rates.Count < segments)
                throw new FormatException($"[配表] skill.csv 技能 {Id} 伤害倍率只有 {rates.Count} 项，不够 {segments} 段用");
            var arr = new float[segments];
            rates.CopyTo(0, arr, 0, segments);
            return arr;
        }

        // 组装子弹命中判定参数：命中球与存活时长取特效预制体，倍率/间隔取配表
        protected BulletHitSpec HitSpec(SkillVfxKind kind, int vfxIndex, float scale)
        {
            var hits = VfxHitSource.GetHits(kind, vfxIndex);
            return new BulletHitSpec(hits, SegmentRates(hits.Length), scale, DamageInterval,
                VfxHitSource.GetLifeTime(kind, vfxIndex), $"{kind}[{vfxIndex}]");
        }

        // 整段共用同一倍率：二次爆炸这类独立效果只从第 11 列取指定项，与命中球段数无关
        protected BulletHitSpec HitSpec(SkillVfxKind kind, int vfxIndex, float scale, float rate)
        {
            var hits = VfxHitSource.GetHits(kind, vfxIndex);
            var rates = new float[hits.Length];
            for (int i = 0; i < rates.Length; i++) rates[i] = rate;
            return new BulletHitSpec(hits, rates, scale, DamageInterval,
                VfxHitSource.GetLifeTime(kind, vfxIndex), $"{kind}[{vfxIndex}]");
        }

        // 无特效近战的球形判定：伤害倍率同样只认 skill.csv 第 11 列（单段取第 0 项，空 = 1）
        protected void StrikeSphere(EntityData entity, Vector3 center, float radius, AttackData attack)
        {
#if UNITY_EDITOR
            DamageRangeDebugHost.Sphere(center, radius); // 调试：可视化本次球形伤害判定范围（渐隐渲染）
#endif
            float rate = ConfigDamageRate();
            int count = EntityPhysics.OverlapCircleXZ(center, radius, s_hitBuffer);
            for (int i = 0; i < count; i++)
            {
                var target = s_hitBuffer[i];
                if (target == null || !target.Alive) continue;
                if (target.id == entity.id || !EntityCampUtil.IsHostile(entity.type.category, target.camp)) continue;
                int damage = attack.GetDamage(out CombatLabelType type, rate);
                target.ProcessHit(attack, damage, type, center); // 击飞方向取判定球心 → 目标
                if (attack.addEffectEvent != null && target.effectController != null)
                {
                    attack.addEffectEvent.Invoke(target.effectController);
                }
                if (attack.onHit != null) attack.onHit.Invoke(target);
            }
        }
        #endregion
    }
}
