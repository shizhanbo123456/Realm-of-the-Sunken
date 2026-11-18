using Ros.Skill.Utils;
using Ros.Transport;
using UnityEngine;

namespace Ros.Skill
{
    // 武器技能（50 个）。槽位 → 技能 的对应是临时顺序分配（刀 11 / 长枪 8 / 枪械 15 / 魔法球 16），
    // 需要调整时只改本文件的注册顺序与 SkillTypes 顺序，不用动实现。
    public static class SkillPoolWeapon
    {
        public static void RegisterAll()
        {
            // 刀（0~10）
            SkillManager.Register(new SkillFanIceCone());
            SkillManager.Register(new SkillTripleThunderCone());
            SkillManager.Register(new SkillFreezeShot());
            SkillManager.Register(new SkillStrengthBreakShot());
            SkillManager.Register(new SkillDoubleFanShot());
            SkillManager.Register(new SkillOrbitBlades());
            SkillManager.Register(new SkillBackstepShot());
            SkillManager.Register(new SkillLeapScatter());
            SkillManager.Register(new SkillThrowWeapon());
            SkillManager.Register(new SkillFanWeaponThrow());
            SkillManager.Register(new SkillSkyfallWeapon());

            // 长枪（11~18）
            SkillManager.Register(new SkillBoomerangWeapon());
            SkillManager.Register(new SkillBackstabWeapon());
            SkillManager.Register(new SkillSwiftAura());
            SkillManager.Register(new SkillOrbitWeaponKnockback());
            SkillManager.Register(new SkillShortDash());
            SkillManager.Register(new SkillShieldTen());
            SkillManager.Register(new SkillFlyingKick());
            SkillManager.Register(new SkillIceFanTiny());

            // 枪械（19~33）
            SkillManager.Register(new SkillBackVolley());
            SkillManager.Register(new SkillIceSweep());
            SkillManager.Register(new SkillRootShot());
            SkillManager.Register(new SkillDriftOrb());
            SkillManager.Register(new SkillTripleWaveIce());
            SkillManager.Register(new SkillForwardSpray());
            SkillManager.Register(new SkillSilenceBolt());
            SkillManager.Register(new SkillSkyfallVolley());
            SkillManager.Register(new SkillOrbitWeaponVolley());
            SkillManager.Register(new SkillOrbitWeaponRain());
            SkillManager.Register(new SkillPullSmash());
            SkillManager.Register(new SkillRainBarrage());
            SkillManager.Register(new SkillDashSpeed());
            SkillManager.Register(new SkillAllyShieldEight());
            SkillManager.Register(new SkillShieldFour());

            // 魔法球（34~49）
            SkillManager.Register(new SkillSplitShot());
            SkillManager.Register(new SkillPullBolt());
            SkillManager.Register(new SkillStaticBarrage());
            SkillManager.Register(new SkillFanFreeze());
            SkillManager.Register(new SkillSweepPoison());
            SkillManager.Register(new SkillBounceShot());
            SkillManager.Register(new SkillSweepBurn());
            SkillManager.Register(new SkillCircleVolley());
            SkillManager.Register(new SkillHookFan());
            SkillManager.Register(new SkillVortexBomb());
            SkillManager.Register(new SkillSixOrbs());
            SkillManager.Register(new SkillSkyfallSilence());
            SkillManager.Register(new SkillBlastKnockback());
            SkillManager.Register(new SkillBackArcWeapons());
            SkillManager.Register(new SkillAllyCritBoost());
            SkillManager.Register(new SkillAllyShieldTwentyFive());
        }
    }

    #region 刀
    public class SkillFanIceCone : SkillBase
    {
        public override int Id => (int)SkillType.SkillFanIceCone;

        private const int Vfx = 2;                 // Projection 蓝·样式3
        private const int Shots = 5;
        private const float SpreadDeg = 14f;
        private const float ShotGap = 0.06f;
        private const float Amplitude = 0.35f;
        private const float Cycles = 1.5f;
        private const float BulletLife = 0.5f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int seed = SkillSeed.New();
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Projection, Vfx, VfxScale());
            Spread.SineFan(origin, aim, seed, SpreadDeg, Shots, ShotGap, Amplitude, Cycles, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); context.WriteInt(seed); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            int seed = context.ReadInt();
            Spread.SineFan(origin, aim, seed, SpreadDeg, Shots, ShotGap, Amplitude, Cycles, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Projection, Vfx, trajectory, VfxScale()));
        }
    }

    public class SkillTripleThunderCone : SkillBase
    {
        public override int Id => (int)SkillType.SkillTripleThunderCone;

        private const int Vfx = 43;                // Projection 黄·样式4
        private const int Shots = 5;
        private const float SpreadDeg = 4f;
        private const float ShotGap = 0.05f;
        private const float BulletLife = 0.45f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int seed = SkillSeed.New();
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Projection, Vfx, VfxScale());
            Spread.MachineGun(origin, aim, seed, SpreadDeg, Shots, ShotGap, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); context.WriteInt(seed); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            int seed = context.ReadInt();
            Spread.MachineGun(origin, aim, seed, SpreadDeg, Shots, ShotGap, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Projection, Vfx, trajectory, VfxScale()));
        }
    }

    public class SkillFreezeShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillFreezeShot;

        private const int Vfx = 6;                 // Projection 蓝·样式7
        private const float Hold = 0.2f;
        private const float Flight = 0.3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Projection, Vfx, VfxScale());
            Tool.BattleManager.ShootBullet(caster, attack, new SkillTrajectoryHoldLine(origin, aim, Hold, Flight), 0f, spec);
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            VfxHelper.PlayOne(SkillVfxKind.Projection, Vfx, new SkillTrajectoryHoldLine(origin, aim, Hold, Flight), VfxScale());
        }
    }

    public class SkillStrengthBreakShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillStrengthBreakShot;

        private const int Vfx = 24;                // Projection 紫·样式5
        private const int Shots = 3;
        private const float Radius = 0.5f;
        private const float Hold = 0.15f;
        private const float Flight = 0.3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int seed = SkillSeed.New();
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Projection, Vfx, VfxScale());
            Spread.ChargeSweep(origin, aim, seed, Radius, Shots, Hold, Flight,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, 0f, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); context.WriteInt(seed); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            int seed = context.ReadInt();
            Spread.ChargeSweep(origin, aim, seed, Radius, Shots, Hold, Flight,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Projection, Vfx, trajectory, VfxScale()));
        }
    }

    // 第一波单发直出，第二波两发 30° 分开
    public class SkillDoubleFanShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillDoubleFanShot;

        private const int Vfx = 8;                 // Projection 蓝·样式9
        private const int Shots = 1;
        private const int Waves = 2;
        private const float WaveGap = 0.18f;
        private const float BulletLife = 0.5f;

        private static int ShotsOfWave(int wave) => wave == 0 ? 1 : 2;
        private static float SpreadOfWave(int wave) => wave == 0 ? 0f : 30f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Projection, Vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, Waves, 0f, WaveGap, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec),
                ShotsOfWave, SpreadOfWave);
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, Waves, 0f, WaveGap, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Projection, Vfx, trajectory, VfxScale()),
                ShotsOfWave, SpreadOfWave);
        }
    }

    // 四发武器绕自身旋转，不索敌
    public class SkillOrbitBlades : SkillBase
    {
        public override int Id => (int)SkillType.SkillOrbitBlades;

        private const int Shots = 4;
        private const float Radius = 2.2f;
        private const float Turns = 1.5f;
        private const float BulletLife = 2.4f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, WeaponOf(caster), VfxScale());
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryOrbit(caster.id, origin, Radius, i * 360f / Shots, Turns, BulletLife),
                    BulletLife, spec);
            }
            SendWave(caster, 0, context => context.WriteVector3(origin));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context),
                    new SkillTrajectoryOrbit(context.caster.id, origin, Radius, i * 360f / Shots, Turns, BulletLife),
                    VfxScale());
            }
        }
    }

    // 后撤闪避（短暂闪避 buff）后向前一发
    public class SkillBackstepShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillBackstepShot;

        private const float BulletLife = 0.5f;
        private const float BackDash = 0.25f;

        protected override void OnCast(EntityData caster)
        {
            caster.SetMotion(new MotionDash(BackDash, -9f));
            ApplyConfigBuff(caster, caster, 0);
            Vector3 origin = OriginPos(caster, EntityAnchor.RightHand, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int vfx = WeaponOf(caster);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, vfx, VfxScale());
            Spread.Fan(origin, aim, 1, 1, 0f, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, 1, 1, 0f, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context), trajectory, VfxScale()));
        }
    }

    // 跃起，朝身前 120° 扇形散射
    public class SkillLeapScatter : SkillBase
    {
        public override int Id => (int)SkillType.SkillLeapScatter;

        private const int Vfx = 41;                // Projection 黄·样式2
        private const int Shots = 12;
        private const float SpreadDeg = 120f;      // 身前半扇：±60°
        private const float BulletLife = 0.9f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Projection, Vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Projection, Vfx, trajectory, VfxScale()));
        }
    }

    public class SkillThrowWeapon : SkillBase
    {
        public override int Id => (int)SkillType.SkillThrowWeapon;

        private const int Shots = 1;
        private const float SpreadDeg = 0f;
        private const float BulletLife = 0.6f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.RightHand, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int vfx = WeaponOf(caster);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context), trajectory, VfxScale()));
        }
    }

    public class SkillFanWeaponThrow : SkillBase
    {
        public override int Id => (int)SkillType.SkillFanWeaponThrow;

        private const int Shots = 5;
        private const float SpreadDeg = 22f;
        private const float BulletLife = 0.65f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int vfx = WeaponOf(caster);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context), trajectory, VfxScale()));
        }
    }

    public class SkillSkyfallWeapon : SkillBase
    {
        public override int Id => (int)SkillType.SkillSkyfallWeapon;

        private const int Shots = 1;
        private const float BulletLife = 1.1f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int vfx = WeaponOf(caster);
            Tool.BattleManager.ShootBullet(caster, BuildAttack(caster),
                new SkillTrajectorySkyFall(origin, aim, BulletLife), BulletLife,
                HitSpec(SkillVfxKind.Weapon, vfx, VfxScale()));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context),
                new SkillTrajectorySkyFall(origin, aim, BulletLife), VfxScale());
        }
    }
    #endregion
    #region 长枪
    public class SkillBoomerangWeapon : SkillBase
    {
        public override int Id => (int)SkillType.SkillBoomerangWeapon;

        private const float BulletLife = 1.6f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.RightHand, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int vfx = WeaponOf(caster);
            Tool.BattleManager.ShootBullet(caster, BuildAttack(caster),
                new SkillTrajectoryBoomerang(caster.id, origin, aim), BulletLife,
                HitSpec(SkillVfxKind.Weapon, vfx, VfxScale()));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context),
                new SkillTrajectoryBoomerang(context.caster.id, origin, aim), VfxScale());
        }
    }

    // 后方飞出扎向敌人，命中麻痹；到达后原地静止漂浮一段
    public class SkillBackstabWeapon : SkillBase
    {
        public override int Id => (int)SkillType.SkillBackstabWeapon;

        private const int Shots = 1;
        private const float BulletLife = 0.55f;
        private const float HoverTime = 1.2f;
        private static readonly Vector3 Offset = new Vector3(0f, 0f, -2f);

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Offset);
            Vector3 aim = Aim(caster, origin);
            int vfx = WeaponOf(caster);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Weapon, vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, 0f, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, 0f, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context), trajectory, VfxScale()));
            // 命中后原地静止漂浮：纯表现，不参与判定
            VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context),
                new SkillTrajectoryPoint(aim, HoverTime), VfxScale());
        }
    }

    public class SkillSwiftAura : SkillBase
    {
        public override int Id => (int)SkillType.SkillSwiftAura;

        protected override void OnCast(EntityData caster) => ApplyConfigBuff(caster, caster, 0);
    }

    public class SkillOrbitWeaponKnockback : SkillBase
    {
        public override int Id => (int)SkillType.SkillOrbitWeaponKnockback;

        private const int Shots = 4;
        private const float Radius = 2f;
        private const float Turns = 1.25f;
        private const float BulletLife = 2f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            int vfx = WeaponOf(caster);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, vfx, VfxScale());
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryOrbit(caster.id, origin, Radius, i * 360f / Shots, Turns, BulletLife),
                    BulletLife, spec);
            }
            SendWave(caster, 0, context => context.WriteVector3(origin));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.Weapon, DisplayWeapon(context),
                    new SkillTrajectoryOrbit(context.caster.id, origin, Radius, i * 360f / Shots, Turns, BulletLife),
                    VfxScale());
            }
        }
    }

    public class SkillShortDash : SkillBase
    {
        public override int Id => (int)SkillType.SkillShortDash;

        private const float DashTime = 0.22f;
        private const float DashSpeed = 22f;       // 明显高于跑步速度，肉眼能看出冲刺

        protected override void OnCast(EntityData caster) => caster.SetMotion(new MotionDash(DashTime, DashSpeed));
    }

    public class SkillShieldTen : SkillBase
    {
        public override int Id => (int)SkillType.SkillShieldTen;

        protected override void OnCast(EntityData caster) => ApplyConfigBuff(caster, caster, 0);
    }

    // 飞踢：前冲位移 + 跟随自身的判定球，全程结算；无特效，表现即角色本体
    public class SkillFlyingKick : SkillBase
    {
        public override int Id => (int)SkillType.SkillFlyingKick;

        private const float KickTime = 1.5f;   // 判定窗：跟随子弹存活时长
        private const float DashTime = 0.3f;   // 前冲时长
        private const float DashSpeed = 12f;   // 前冲速度

        protected override void OnCast(EntityData caster)
        {
            caster.SetMotion(new MotionDash(DashTime, DashSpeed));
            var hit = new BulletVfxInfo.HitInfo { center = Vector3.zero, radius = MeleeRadius }; // end 留 0 = 全程有效
            var spec = new BulletHitSpec(new[] { hit }, new[] { ConfigDamageRate() }, 1f, DamageInterval, 0f, "飞踢[跟随]");
            Tool.BattleManager.ShootBullet(caster, BuildAttack(caster), new SkillTrajectoryFollow(caster.id), KickTime, spec);
        }
    }

    public class SkillIceFanTiny : SkillBase
    {
        public override int Id => (int)SkillType.SkillIceFanTiny;

        private const int Vfx = 7;                 // Slash 蓝白
        private const int Shots = 4;
        private const float SpreadDeg = 8f;
        private const float BulletLife = 0.5f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Slash, Vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Slash, Vfx, trajectory, VfxScale()));
        }
    }
    #endregion
    #region 枪械
    // 后方随机点创建子弹，集中射向前方一点
    public class SkillBackVolley : SkillBase
    {
        public override int Id => (int)SkillType.SkillBackVolley;

        private const int Vfx = 3;                 // 华丽飞弹蓝
        private const int Shots = 6;
        private const float BulletLife = 0.9f;
        private const float BehindMin = 1.5f;
        private const float BehindMax = 3.5f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 center = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, center);
            int seed = SkillSeed.New();
            Quaternion rotation = caster.transform.rotation;
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            var rng = SkillSeed.Rng(seed);
            for (int i = 0; i < Shots; i++)
            {
                Vector3 start = BackPoint(center, rotation, rng);
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryLine(start, aim, BulletLife), BulletLife, spec);
            }
            SendWave(caster, 0, context => { context.WriteVector3(center); context.WriteVector3(aim); context.WriteInt(seed); });
        }

        private static Vector3 BackPoint(Vector3 center, Quaternion rotation, System.Random rng)
        {
            float back = BehindMin + (float)rng.NextDouble() * (BehindMax - BehindMin);
            float side = ((float)rng.NextDouble() - 0.5f) * 3f;
            return center + rotation * new Vector3(side, 0f, -back);
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 center = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            int seed = context.ReadInt();
            // 朝向同样按实体 id 取实时值，查不到时按无旋转处理
            Quaternion rotation = BulletTrajectory.TryGetEntityTransform(context.caster.id, out _, out var rot)
                ? rot
                : Quaternion.identity;
            var rng = SkillSeed.Rng(seed);
            for (int i = 0; i < Shots; i++)
            {
                Vector3 start = BackPoint(center, rotation, rng);
                VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx,
                    new SkillTrajectoryLine(start, aim, BulletLife), VfxScale());
            }
        }
    }

    public class SkillIceSweep : SkillBase
    {
        public override int Id => (int)SkillType.SkillIceSweep;

        private const int Vfx = 9;                 // 小飞弹蓝
        private const int Waves = 4;
        private const float WaveGap = 0.1f;
        private const float BulletLife = 0.45f;

        private static float SpreadOfWave(int wave) => (wave - 1.5f) * 8f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, 1, Waves, 0f, WaveGap, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec),
                null, SpreadOfWave);
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, 1, Waves, 0f, WaveGap, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()),
                null, SpreadOfWave);
        }
    }

    public class SkillRootShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillRootShot;

        private const int Vfx = 45;                // 大锥子蓝
        private const float Hold = 0.25f;
        private const float Flight = 0.3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Tool.BattleManager.ShootBullet(caster, attack, new SkillTrajectoryHoldLine(origin, aim, Hold, Flight), 0f, spec);
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, new SkillTrajectoryHoldLine(origin, aim, Hold, Flight), VfxScale());
        }
    }

    // 浮游弹：缓行弹体，30° 扇形齐射 5 发
    public class SkillDriftOrb : SkillBase
    {
        public override int Id => (int)SkillType.SkillDriftOrb;

        private const int Vfx = 12;                // 华丽飞弹蓝
        private const int Shots = 5;
        private const float SpreadDeg = 30f;
        private const float BulletLife = 4.8f;
        private const float ArcHeight = 3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Vector3[] dests = SpreadStyle.FanDests(origin, aim, Shots, SpreadDeg);
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryBezier(origin, dests[i], BulletLife, ArcHeight), BulletLife, spec);
            }
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Vector3[] dests = SpreadStyle.FanDests(origin, aim, Shots, SpreadDeg);
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx,
                    new SkillTrajectoryBezier(origin, dests[i], BulletLife, ArcHeight), VfxScale());
            }
        }
    }

    // 三波递增发数：3 / 5 / 7
    public class SkillTripleWaveIce : SkillBase
    {
        public override int Id => (int)SkillType.SkillTripleWaveIce;

        private const int Vfx = 46;                // 大锥子橙
        private const int Waves = 3;
        private const float SpreadDeg = 25f;
        private const float WaveGap = 0.2f;
        private const float BulletLife = 0.5f;

        private static int ShotsOfWave(int wave) => 3 + wave * 2;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, 3, Waves, SpreadDeg, WaveGap, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec),
                ShotsOfWave, null);
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, 3, Waves, SpreadDeg, WaveGap, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()),
                ShotsOfWave, null);
        }
    }

    public class SkillForwardSpray : SkillBase
    {
        public override int Id => (int)SkillType.SkillForwardSpray;

        private const int Vfx = 10;                // 小飞弹绿
        private const int Shots = 5;
        private const int Waves = 3;
        private const float SpreadDeg = 35f;
        private const float WaveGap = 0.12f;
        private const float BulletLife = 0.45f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, Waves, SpreadDeg, WaveGap, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, Waves, SpreadDeg, WaveGap, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()));
        }
    }

    public class SkillSilenceBolt : SkillBase
    {
        public override int Id => (int)SkillType.SkillSilenceBolt;

        private const int Vfx = 19;                // 锥子黄
        private const float BulletLife = 0.25f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpFront, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, 1, 1, 0f, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, 1, 1, 0f, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()));
        }
    }

    // 天罗：自身周围 5 个点各自天降，起点与落点都在自己脚下周围
    public class SkillSkyfallVolley : SkillBase
    {
        public override int Id => (int)SkillType.SkillSkyfallVolley;

        private const int Vfx = 2;
        private const int Shots = 5;
        private const float Ring = 4f;
        private const float BulletLife = 1.1f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 center = caster.transform.position; // 落点中心是自己脚下
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.RangeMagic, Vfx, VfxScale());
            Vector3[] dests = SpreadStyle.CircleDests(Vector3.zero, Ring, Shots);
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectorySkyFall(center, center + dests[i], BulletLife), BulletLife, spec);
            }
            SendWave(caster, 0, context => context.WriteVector3(center));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 center = context.ReadVector3();
            Vector3[] dests = SpreadStyle.CircleDests(Vector3.zero, Ring, Shots);
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.RangeMagic, Vfx,
                    new SkillTrajectorySkyFall(center, center + dests[i], BulletLife), VfxScale());
            }
        }
    }

    // 环射：每波从环身的不同方位切向射出
    public class SkillOrbitWeaponVolley : SkillBase
    {
        public override int Id => (int)SkillType.SkillOrbitWeaponVolley;

        private const int Shots = 2;
        private const int Waves = 3;
        private const float SpreadDeg = 10f;
        private const float WaveGap = 0.2f;
        private const float BulletLife = 0.5f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, WeaponOf(caster), VfxScale());
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) FireWave(caster, origin, aim, 0, attack, spec);
                else Timer.AddTimer(w, _ => FireWave(caster, origin, aim, w, attack, spec), WaveGap * w, 1, false);
            }
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        private void FireWave(EntityData caster, Vector3 center, Vector3 aim, int wave, AttackData attack, BulletHitSpec spec)
        {
            // 环身取点：12 等分即每发间隔 30°，波次整体再转 60°
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, 1.5f, 12, wave * 60f);
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryLine(center + ring[i], aim, BulletLife), BulletLife, spec);
            }
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 center = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            int weapon = DisplayWeapon(context);
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) PlayOneWave(center, aim, 0, weapon);
                else Timer.AddTimer(w, _ => PlayOneWave(center, aim, w, weapon), WaveGap * w, 1, false);
            }
        }

        private void PlayOneWave(Vector3 center, Vector3 aim, int wave, int weapon)
        {
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, 1.5f, 12, wave * 60f);
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.Weapon, weapon,
                    new SkillTrajectoryLine(center + ring[i], aim, BulletLife), VfxScale());
            }
        }
    }

    // 兵雨：自身周围 6 个点循环天降
    public class SkillOrbitWeaponRain : SkillBase
    {
        public override int Id => (int)SkillType.SkillOrbitWeaponRain;

        private const int Shots = 3;
        private const int Waves = 3;
        private const float WaveGap = 0.25f;
        private const float BulletLife = 1f;
        private const float Ring = 3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 center = caster.transform.position; // 落点中心是自己脚下
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Weapon, WeaponOf(caster), VfxScale());
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) FireWave(caster, center, 0, attack, spec);
                else Timer.AddTimer(w, _ => FireWave(caster, center, w, attack, spec), WaveGap * w, 1, false);
            }
            SendWave(caster, 0, context => context.WriteVector3(center));
        }

        private void FireWave(EntityData caster, Vector3 center, int wave, AttackData attack, BulletHitSpec spec)
        {
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, 6);
            for (int i = 0; i < Shots; i++)
            {
                Vector3 land = center + ring[(wave * Shots + i) % 6];
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectorySkyFall(center + Vector3.up * 8f, land, BulletLife), BulletLife, spec);
            }
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 center = context.ReadVector3();
            int weapon = DisplayWeapon(context);
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) PlayOneWave(center, 0, weapon);
                else Timer.AddTimer(w, _ => PlayOneWave(center, w, weapon), WaveGap * w, 1, false);
            }
        }

        private void PlayOneWave(Vector3 center, int wave, int weapon)
        {
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, 6);
            for (int i = 0; i < Shots; i++)
            {
                Vector3 land = center + ring[(wave * Shots + i) % 6];
                VfxHelper.PlayOne(SkillVfxKind.Weapon, weapon,
                    new SkillTrajectorySkyFall(center + Vector3.up * 8f, land, BulletLife), VfxScale());
            }
        }
    }

    // 引力锤击：把周围敌人拉到自身位置，半秒后原地砸击
    public class SkillPullSmash : SkillBase
    {
        public override int Id => (int)SkillType.SkillPullSmash;

        private const int PullVfx = 1;             // Bullet 黑洞紫：牵引阶段的表现
        private const int LandVfx = 7;
        private const float PullTime = 0.5f;
        private const float PullVfxLife = 1f;
        private const float LandVfxLife = 0.8f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 center = caster.transform.position;
            CollectEnemies(caster, center);
            for (int i = 0; i < TargetSelect.TargetBuffer.Count; i++)
            {
                EntityData target = TargetSelect.TargetBuffer[i];
                if (target != null) target.SetMotion(new MotionAttract(center, PullTime)); // 牵引点 = 施放瞬间的位置
            }
            Timer.AddTimer(0, _ =>
            {
                if (caster == null) return;
                // 落点伤害由 RM[7] 的命中球决定：子弹定在落点陪特效走
                Tool.BattleManager.ShootBullet(caster, BuildAttack(caster),
                    new SkillTrajectoryPoint(caster.transform.position, LandVfxLife), LandVfxLife,
                    HitSpec(SkillVfxKind.RangeMagic, LandVfx, VfxScale(1)));
            }, PullTime, 1, false);
            SendWave(caster, 0, context => context.WriteVector3(center));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 center = context.ReadVector3();
            VfxHelper.PlayAt(SkillVfxKind.Bullet, PullVfx, center, PullVfxLife, VfxScale(0));
            Timer.AddTimer(0, _ => VfxHelper.PlayAt(SkillVfxKind.RangeMagic, LandVfx, center, LandVfxLife, VfxScale(1)),
                PullTime, 1, false);
        }
    }

    // 坠落序曲：落点取射程内最近的敌人，每波 1 发中心 + 3 发外圈
    public class SkillRainBarrage : SkillBase
    {
        public override int Id => (int)SkillType.SkillRainBarrage;

        private const int Vfx = 7;                 // RangeMagic 连续岩浆爆炸
        private const int Shots = 4;
        private const int Waves = 3;
        private const float WaveGap = 0.25f;
        private const float BulletLife = 0.9f;
        private const float Ring = 4f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 center = LandCenter(caster);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.RangeMagic, Vfx, VfxScale());
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) FireWave(caster, center, 0, attack, spec);
                else Timer.AddTimer(w, _ => FireWave(caster, center, w, attack, spec), WaveGap * w, 1, false);
            }
            SendWave(caster, 0, context => context.WriteVector3(center));
        }

        // 弹幕中心 = 射程内最近的敌人脚下；范围内没有敌人时退回前方射程落点
        private Vector3 LandCenter(EntityData caster)
        {
            EntityData target = BattleManager.EntityContainer.GetNearestEnemy(caster, CastRange);
            return target != null ? target.transform.position : Aim(caster, OriginPos(caster, EntityAnchor.UpFront, Vector3.zero));
        }

        private void FireWave(EntityData caster, Vector3 center, int wave, AttackData attack, BulletHitSpec spec)
        {
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, 8);
            for (int i = 0; i < Shots; i++)
            {
                // 第 0 发砸在正中心，贴身敌人也吃得到
                Vector3 land = i == 0 ? center : center + ring[(wave * (Shots - 1) + i - 1) % 8];
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectorySkyFall(center + Vector3.up * 10f, land, BulletLife), BulletLife, spec);
            }
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 center = context.ReadVector3();
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) PlayOneWave(center, 0);
                else Timer.AddTimer(w, _ => PlayOneWave(center, w), WaveGap * w, 1, false);
            }
        }

        private void PlayOneWave(Vector3 center, int wave)
        {
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, 8);
            for (int i = 0; i < Shots; i++)
            {
                Vector3 land = i == 0 ? center : center + ring[(wave * (Shots - 1) + i - 1) % 8];
                VfxHelper.PlayOne(SkillVfxKind.RangeMagic, Vfx,
                    new SkillTrajectorySkyFall(center + Vector3.up * 10f, land, BulletLife), VfxScale());
            }
        }
    }

    public class SkillDashSpeed : SkillBase
    {
        public override int Id => (int)SkillType.SkillDashSpeed;

        protected override void OnCast(EntityData caster)
        {
            caster.SetMotion(new MotionDash(0.35f, 13f));
            ApplyConfigBuff(caster, caster, 0);
        }
    }

    // 同心壁垒：自身与附近友方挂护盾
    public class SkillAllyShieldEight : SkillBase
    {
        public override int Id => (int)SkillType.SkillAllyShieldEight;

        protected override void OnCast(EntityData caster)
        {
            TargetSelect.SetEntitiesInCampToBuffer(caster.camp);
            Vector3 center = caster.transform.position;
            float sqr = TargetRadius * TargetRadius;
            for (int i = 0; i < TargetSelect.TargetBuffer.Count; i++)
            {
                EntityData ally = TargetSelect.TargetBuffer[i];
                if (ally == null || (ally.transform.position - center).sqrMagnitude > sqr) continue;
                ApplyConfigBuff(caster, ally, 0);
            }
        }
    }

    public class SkillShieldFour : SkillBase
    {
        public override int Id => (int)SkillType.SkillShieldFour;

        protected override void OnCast(EntityData caster) => ApplyConfigBuff(caster, caster, 0);
    }
    #endregion
    #region 魔法球
    // 母弹飞到终点后分裂 5 发扇形
    public class SkillSplitShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillSplitShot;

        private const int Vfx = 12;                // 华丽飞弹蓝
        private const int SplitVfx = 14;           // 华丽飞弹黄
        private const int SplitCount = 5;
        private const float SplitSpread = 30f;
        private const float BulletLife = 0.6f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, 1, 1, 0f, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            Timer.AddTimer(0, _ => Split(caster, origin, aim), BulletLife, 1, false);
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        private void Split(EntityData caster, Vector3 origin, Vector3 aim)
        {
            if (caster == null) return;
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, SplitVfx, VfxScale(1));
            Vector3[] dests = SpreadStyle.FanDests(origin, aim, SplitCount, SplitSpread);
            for (int i = 0; i < SplitCount; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryLine(aim, dests[i], BulletLife), BulletLife, spec);
            }
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, 1, 1, 0f, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()));
            Timer.AddTimer(0, _ =>
            {
                Vector3[] dests = SpreadStyle.FanDests(origin, aim, SplitCount, SplitSpread);
                for (int i = 0; i < SplitCount; i++)
                {
                    VfxHelper.PlayOne(SkillVfxKind.Bullet, SplitVfx,
                        new SkillTrajectoryLine(aim, dests[i], BulletLife), VfxScale(1));
                }
            }, BulletLife, 1, false);
        }
    }

    // 命中后把目标持续拉向正在飞的子弹
    public class SkillPullBolt : SkillBase
    {
        public override int Id => (int)SkillType.SkillPullBolt;

        private const int Vfx = 1;
        private const float BulletLife = 0.7f;
        private const float PullDuration = 1.2f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var trajectory = new SkillTrajectoryLine(origin, aim, BulletLife);
            float spawn = Time.time;
            var attack = BuildAttack(caster, onHit: target =>
            {
                if (target == null) return;
                target.SetMotion(new MotionAttract(
                    () => trajectory.Lerp(Mathf.Clamp01((Time.time - spawn) / BulletLife)), PullDuration));
            });
            Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife,
                HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale()));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, new SkillTrajectoryLine(origin, aim, BulletLife), VfxScale());
        }
    }

    // 静滞矩阵：自身周围 12 个定点子弹
    public class SkillStaticBarrage : SkillBase
    {
        public override int Id => (int)SkillType.SkillStaticBarrage;

        private const int Vfx = 47;                // 大锥子红
        private const int Shots = 12;
        private const float Ring = 2.6f;
        private const float BulletLife = 3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, Shots);
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryPoint(origin + ring[i], BulletLife), BulletLife, spec);
            }
            SendWave(caster, 0, context => context.WriteVector3(origin));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, Shots);
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx,
                    new SkillTrajectoryPoint(origin + ring[i], BulletLife), VfxScale());
            }
        }
    }

    public class SkillFanFreeze : SkillBase
    {
        public override int Id => (int)SkillType.SkillFanFreeze;

        private const int Vfx = 9;                 // 小飞弹蓝
        private const int Shots = 5;
        private const float SpreadDeg = 25f;
        private const float BulletLife = 0.55f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()));
        }
    }

    // 来回扫射：6 波按 -25 / 0 / 25 / 25 / 0 / -25 摆动
    public class SkillSweepPoison : SkillBase
    {
        public override int Id => (int)SkillType.SkillSweepPoison;

        private const int Vfx = 10;                // 小飞弹绿
        private const int Waves = 6;
        private const float WaveGap = 0.1f;
        private const float BulletLife = 0.4f;
        private static readonly float[] Angles = { -25f, 0f, 25f, 25f, 0f, -25f };

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) FireWave(caster, origin, aim, 0, attack, spec);
                else Timer.AddTimer(w, _ => FireWave(caster, origin, aim, w, attack, spec), WaveGap * w, 1, false);
            }
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        private void FireWave(EntityData caster, Vector3 origin, Vector3 aim, int wave, AttackData attack, BulletHitSpec spec)
        {
            Vector3 dest = SpreadStyle.RotateTargetAroundDirection(origin, aim, Angles[wave % Angles.Length]);
            Tool.BattleManager.ShootBullet(caster, attack,
                new SkillTrajectoryLine(origin, dest, BulletLife), BulletLife, spec);
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) PlayOneWave(origin, aim, 0);
                else Timer.AddTimer(w, _ => PlayOneWave(origin, aim, w), WaveGap * w, 1, false);
            }
        }

        private void PlayOneWave(Vector3 origin, Vector3 aim, int wave)
        {
            Vector3 dest = SpreadStyle.RotateTargetAroundDirection(origin, aim, Angles[wave % Angles.Length]);
            VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, new SkillTrajectoryLine(origin, dest, BulletLife), VfxScale());
        }
    }

    // 连弹：在周围敌人间弹跳 8 次，施放时算好整串点位随包下发
    public class SkillBounceShot : SkillBase
    {
        public override int Id => (int)SkillType.SkillBounceShot;

        private const int Vfx = 13;                // 华丽飞弹黄
        private const int Jumps = 8;
        private const float JumpLife = 0.18f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 last = caster.transform.position;
            ushort lastId = caster.id;
            var trail = new System.Collections.Generic.List<Vector3> { last };
            for (int i = 0; i < Jumps; i++)
            {
                TargetSelect.SetEnemiesInRangeToBuffer(last, TargetRadius, caster.type.category); // 配表半径 = 每跳搜索半径
                EntityData next = null;
                float best = float.MaxValue;
                for (int j = 0; j < TargetSelect.TargetBuffer.Count; j++)
                {
                    EntityData candidate = TargetSelect.TargetBuffer[j];
                    if (candidate == null || candidate.id == lastId) continue;
                    float dist = (candidate.transform.position - last).sqrMagnitude;
                    if (dist >= best) continue;
                    best = dist;
                    next = candidate;
                }
                if (next == null) break;
                last = next.transform.position;
                lastId = next.id;
                trail.Add(last);
            }
            if (trail.Count < 2) return;
            Vector3[] points = trail.ToArray();
            float duration = JumpLife * (points.Length - 1);
            Tool.BattleManager.ShootBullet(caster, BuildAttack(caster),
                new SkillTrajectoryPolyline(points, duration), duration,
                HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale()));
            SendWave(caster, 0, context =>
            {
                context.WriteInt(points.Length);
                for (int i = 0; i < points.Length; i++) context.WriteVector3(points[i]);
            });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            int count = context.ReadInt();
            if (count < 2) throw new System.Exception($"[技能包] 弹跳点位只有 {count} 个");
            var points = new Vector3[count];
            for (int i = 0; i < count; i++) points[i] = context.ReadVector3();
            float duration = JumpLife * (count - 1);
            VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, new SkillTrajectoryPolyline(points, duration), VfxScale());
        }
    }

    // 焚幕：每两发夹 5°，-60° → +60° → -60° 来回扫射
    public class SkillSweepBurn : SkillBase
    {
        public override int Id => (int)SkillType.SkillSweepBurn;

        private const int Vfx = 56;                // 粗锥子红
        private const int Shots = 49;
        private const float StepDeg = 5f;
        private const float SweepDeg = 60f;
        private const float ShotGap = 0.05f;
        private const float BulletLife = 0.4f;

        // 三角波：前 24 发从 -60° 摆到 +60°，后 24 发摆回 -60°
        private static float AngleOf(int shot)
        {
            int leg = shot % 48;
            int step = leg <= 24 ? leg : 48 - leg;
            return -SweepDeg + StepDeg * step;
        }

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            for (int shot = 0; shot < Shots; shot++)
            {
                int s = shot;
                if (s == 0) Fire(caster, origin, aim, 0, attack, spec);
                else Timer.AddTimer(s, _ => Fire(caster, origin, aim, s, attack, spec), ShotGap * s, 1, false);
            }
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        private void Fire(EntityData caster, Vector3 origin, Vector3 aim, int shot, AttackData attack, BulletHitSpec spec)
        {
            Vector3 dest = SpreadStyle.RotateTargetAroundDirection(origin, aim, AngleOf(shot));
            Tool.BattleManager.ShootBullet(caster, attack,
                new SkillTrajectoryLine(origin, dest, BulletLife), BulletLife, spec);
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            for (int shot = 0; shot < Shots; shot++)
            {
                int s = shot;
                if (s == 0) PlayOne(origin, aim, 0);
                else Timer.AddTimer(s, _ => PlayOne(origin, aim, s), ShotGap * s, 1, false);
            }
        }

        private void PlayOne(Vector3 origin, Vector3 aim, int shot)
        {
            Vector3 dest = SpreadStyle.RotateTargetAroundDirection(origin, aim, AngleOf(shot));
            VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, new SkillTrajectoryLine(origin, dest, BulletLife), VfxScale());
        }
    }

    // 六芒连射：每 60° 一发连成圈，连打三圈（偏航角 = 60° × 序号 % 360°）
    public class SkillCircleVolley : SkillBase
    {
        public override int Id => (int)SkillType.SkillCircleVolley;

        private const int Vfx = 40;                // 刀光橙
        private const int Shots = 18;
        private const float WaveGap = 0.12f;
        private const float BulletLife = 0.6f;
        private const float FireRange = 8f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            for (int shot = 0; shot < Shots; shot++)
            {
                int s = shot;
                if (s == 0) Fire(caster, origin, 0, attack, spec);
                else Timer.AddTimer(s, _ => Fire(caster, origin, s, attack, spec), WaveGap * s, 1, false);
            }
            SendWave(caster, 0, context => context.WriteVector3(origin));
        }

        private void Fire(EntityData caster, Vector3 origin, int shot, AttackData attack, BulletHitSpec spec)
        {
            Vector3 dir = Quaternion.Euler(0f, shot * 60f % 360f, 0f) * Vector3.forward;
            Tool.BattleManager.ShootBullet(caster, attack,
                new SkillTrajectoryLine(origin, origin + dir * FireRange, BulletLife), BulletLife, spec);
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            for (int shot = 0; shot < Shots; shot++)
            {
                int s = shot;
                if (s == 0) PlayOne(origin, 0);
                else Timer.AddTimer(s, _ => PlayOne(origin, s), WaveGap * s, 1, false);
            }
        }

        private void PlayOne(Vector3 origin, int shot)
        {
            Vector3 dir = Quaternion.Euler(0f, shot * 60f % 360f, 0f) * Vector3.forward;
            VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx,
                new SkillTrajectoryLine(origin, origin + dir * FireRange, BulletLife), VfxScale());
        }
    }

    // 瞬影扇：朝身前扇形甩出刀光，纯伤害不牵引
    public class SkillHookFan : SkillBase
    {
        public override int Id => (int)SkillType.SkillHookFan;

        private const int Vfx = 41;                // 刀光紫
        private const int Shots = 5;
        private const float SpreadDeg = 28f;
        private const float BulletLife = 0.5f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => Tool.BattleManager.ShootBullet(caster, attack, trajectory, BulletLife, spec));
            SendWave(caster, 0, context => { context.WriteVector3(origin); context.WriteVector3(aim); });
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3 aim = context.ReadVector3();
            Spread.Fan(origin, aim, Shots, 1, SpreadDeg, 0f, BulletLife,
                trajectory => VfxHelper.PlayOne(SkillVfxKind.Bullet, Vfx, trajectory, VfxScale()));
        }
    }

    // 涡爆：涡心落在射程内最近的敌人脚下，持续吸引后延迟爆炸
    public class SkillVortexBomb : SkillBase
    {
        public override int Id => (int)SkillType.SkillVortexBomb;

        private const int Vfx = 1;
        private const float PullTime = 1.2f;
        private const float FrontDistance = 5f;

        protected override void OnCast(EntityData caster)
        {
            EntityData target = BattleManager.EntityContainer.GetNearestEnemy(caster, CastRange);
            Vector3 center = target != null
                ? target.transform.position
                : caster.transform.position + caster.transform.forward * FrontDistance;
            CollectEnemies(caster, center);
            for (int i = 0; i < TargetSelect.TargetBuffer.Count; i++)
            {
                EntityData enemy = TargetSelect.TargetBuffer[i];
                if (enemy != null) enemy.SetMotion(new MotionAttract(center, PullTime)); // 牵引点 = 施放瞬间的位置
            }
            // 引爆时刻由 RM[1] 的命中窗口决定（1.0~1.2s），子弹在施放时就位陪特效走完整段
            Tool.BattleManager.ShootBullet(caster, BuildAttack(caster),
                new SkillTrajectoryPoint(center, PullTime), PullTime,
                HitSpec(SkillVfxKind.RangeMagic, Vfx, VfxScale()));
            SendWave(caster, 0, context => context.WriteVector3(center));
        }

        public override void PlayWave(SCSkillDisplayContext context)
            => VfxHelper.PlayAt(SkillVfxKind.RangeMagic, Vfx, context.ReadVector3(), PullTime, VfxScale());
    }

    // 六点灼阵：自身周围 6 个定点
    public class SkillSixOrbs : SkillBase
    {
        public override int Id => (int)SkillType.SkillSixOrbs;

        private const int Vfx = 9;                 // RangeMagic 血刺法阵
        private const int Shots = 6;
        private const float Ring = 3f;
        private const float BulletLife = 3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.RangeMagic, Vfx, VfxScale());
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, Shots);
            for (int i = 0; i < Shots; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryPoint(origin + ring[i], BulletLife), BulletLife, spec);
            }
            SendWave(caster, 0, context => context.WriteVector3(origin));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 origin = context.ReadVector3();
            Vector3[] ring = SpreadStyle.CircleDests(Vector3.zero, Ring, Shots);
            for (int i = 0; i < Shots; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.RangeMagic, Vfx,
                    new SkillTrajectoryPoint(origin + ring[i], BulletLife), VfxScale());
            }
        }
    }

    public class SkillSkyfallSilence : SkillBase
    {
        public override int Id => (int)SkillType.SkillSkyfallSilence;

        private const int Vfx = 6;                 // RangeMagic 水墨爆炸
        private const float BulletLife = 1.1f;
        private const float DropHeight = 10f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            // 落点取索敌目标脚下：不按射程归一化，贴身敌人也砸得到
            EntityData target = ResolveTarget(caster);
            Vector3 land = target != null ? target.transform.position : Aim(caster, origin);
            Tool.BattleManager.ShootBullet(caster, BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0)),
                new SkillTrajectorySkyFall(land + Vector3.up * DropHeight, land, BulletLife), BulletLife,
                HitSpec(SkillVfxKind.RangeMagic, Vfx, VfxScale()));
            SendWave(caster, 0, context => context.WriteVector3(land));
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 land = context.ReadVector3();
            VfxHelper.PlayOne(SkillVfxKind.RangeMagic, Vfx,
                new SkillTrajectorySkyFall(land + Vector3.up * DropHeight, land, BulletLife), VfxScale());
        }
    }

    // 黑洞爆破：自身前后左右各布一个黑洞，持续 6 秒不断把附近的敌人吸进去
    public class SkillBlastKnockback : SkillBase
    {
        public override int Id => (int)SkillType.SkillBlastKnockback;

        private const int Vfx = 2;                 // Bullet 黑洞红
        private const int Fields = 4;
        private const float FieldDistance = 3.5f;
        private const float FieldLife = 6f;
        private const float PullInterval = 0.4f;
        private const float PullWindow = 0.5f;
        private const int PullTicks = 15;

        protected override void OnCast(EntityData caster)
        {
            Vector3 pos = caster.transform.position;
            Vector3[] points = FieldPoints(pos, caster.transform.rotation);
            var attack = BuildAttack(caster, addEffect: ConfigHitBuff(caster, 0));
            var spec = HitSpec(SkillVfxKind.Bullet, Vfx, VfxScale());
            for (int i = 0; i < Fields; i++)
            {
                Vector3 point = points[i];
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryPoint(point, FieldLife), FieldLife, spec);
                Timer.AddTimer(0, _ => PullAround(caster, point), PullInterval, PullTicks, true);
            }
            SendWave(caster, 0, context =>
            {
                for (int i = 0; i < Fields; i++) context.WriteVector3(points[i]);
            });
        }

        // 前 / 后 / 左 / 右：以施法瞬间的朝向为基准
        private static Vector3[] FieldPoints(Vector3 pos, Quaternion rotation) => new[]
        {
            pos + rotation * new Vector3(0f, 0f, FieldDistance),
            pos + rotation * new Vector3(0f, 0f, -FieldDistance),
            pos + rotation * new Vector3(FieldDistance, 0f, 0f),
            pos + rotation * new Vector3(-FieldDistance, 0f, 0f),
        };

        // 每隔一小段重新施加牵引，保证走进来的敌人也会被持续拉向黑洞
        private void PullAround(EntityData caster, Vector3 center)
        {
            if (caster == null) return;
            TargetSelect.SetEnemiesInRangeToBuffer(center, TargetRadius, caster.type.category);
            for (int i = 0; i < TargetSelect.TargetBuffer.Count; i++)
            {
                EntityData enemy = TargetSelect.TargetBuffer[i];
                if (enemy != null) enemy.SetMotion(new MotionAttract(center, PullWindow));
            }
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            for (int i = 0; i < Fields; i++)
                VfxHelper.PlayAt(SkillVfxKind.Bullet, Vfx, context.ReadVector3(), FieldLife, VfxScale());
        }
    }

    // 弧刃背袭：水平刀光从身后左右两侧的随机位置掠向目标
    public class SkillBackArcWeapons : SkillBase
    {
        public override int Id => (int)SkillType.SkillBackArcWeapons;

        private const int Vfx = 2;                 // Slash 深红刀光
        private const int Shots = 6;
        private const int Waves = 3;
        private const int ShotsPerWave = Shots / Waves;
        private const float WaveGap = 0.12f;
        private const float BulletLife = 0.45f;
        private const float BehindMin = 0.8f;
        private const float BehindMax = 2.2f;
        private const float SideMin = 1.2f;
        private const float SideMax = 3f;

        protected override void OnCast(EntityData caster)
        {
            Vector3 origin = OriginPos(caster, EntityAnchor.UpCenter, Vector3.zero);
            Vector3 aim = Aim(caster, origin);
            int seed = SkillSeed.New();
            Vector3[] starts = StartPoints(caster, aim, seed);
            var attack = BuildAttack(caster);
            var spec = HitSpec(SkillVfxKind.Slash, Vfx, VfxScale());
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) FireWave(caster, starts, aim, 0, attack, spec);
                else Timer.AddTimer(w, _ => FireWave(caster, starts, aim, w, attack, spec), WaveGap * w, 1, false);
            }
            SendWave(caster, 0, context =>
            {
                context.WriteVector3(aim);
                for (int i = 0; i < Shots; i++) context.WriteVector3(starts[i]);
            });
        }

        // 起手点：左右交替、落在身后两侧，高度与落点齐平所以刀光是水平掠过去的
        private static Vector3[] StartPoints(EntityData caster, Vector3 aim, int seed)
        {
            var points = new Vector3[Shots];
            var rng = SkillSeed.Rng(seed);
            Vector3 pos = caster.transform.position;
            Quaternion rotation = caster.transform.rotation;
            for (int i = 0; i < Shots; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f;
                float back = BehindMin + (float)rng.NextDouble() * (BehindMax - BehindMin);
                float width = SideMin + (float)rng.NextDouble() * (SideMax - SideMin);
                Vector3 point = pos + rotation * new Vector3(side * width, 0f, -back);
                point.y = aim.y;
                points[i] = point;
            }
            return points;
        }

        private void FireWave(EntityData caster, Vector3[] starts, Vector3 aim, int wave, AttackData attack, BulletHitSpec spec)
        {
            for (int i = 0; i < ShotsPerWave; i++)
            {
                Tool.BattleManager.ShootBullet(caster, attack,
                    new SkillTrajectoryLine(starts[wave * ShotsPerWave + i], aim, BulletLife), BulletLife, spec);
            }
        }

        public override void PlayWave(SCSkillDisplayContext context)
        {
            Vector3 aim = context.ReadVector3();
            var starts = new Vector3[Shots];
            for (int i = 0; i < Shots; i++) starts[i] = context.ReadVector3();
            for (int wave = 0; wave < Waves; wave++)
            {
                int w = wave;
                if (w == 0) PlayOneWave(starts, aim, 0);
                else Timer.AddTimer(w, _ => PlayOneWave(starts, aim, w), WaveGap * w, 1, false);
            }
        }

        private void PlayOneWave(Vector3[] starts, Vector3 aim, int wave)
        {
            for (int i = 0; i < ShotsPerWave; i++)
            {
                VfxHelper.PlayOne(SkillVfxKind.Slash, Vfx,
                    new SkillTrajectoryLine(starts[wave * ShotsPerWave + i], aim, BulletLife), VfxScale());
            }
        }
    }

    // 锐意共鸣：自身与附近友方挂暴击率 + 暴伤
    public class SkillAllyCritBoost : SkillBase
    {
        public override int Id => (int)SkillType.SkillAllyCritBoost;

        protected override void OnCast(EntityData caster)
        {
            TargetSelect.SetEntitiesInCampToBuffer(caster.camp);
            Vector3 center = caster.transform.position;
            float sqr = TargetRadius * TargetRadius;
            for (int i = 0; i < TargetSelect.TargetBuffer.Count; i++)
            {
                EntityData ally = TargetSelect.TargetBuffer[i];
                if (ally == null || (ally.transform.position - center).sqrMagnitude > sqr) continue;
                ApplyConfigBuff(caster, ally, 0);
                ApplyConfigBuff(caster, ally, 1);
            }
        }
    }

    // 圣域壁垒：全体友方挂护盾
    public class SkillAllyShieldTwentyFive : SkillBase
    {
        public override int Id => (int)SkillType.SkillAllyShieldTwentyFive;

        protected override void OnCast(EntityData caster)
        {
            TargetSelect.SetEntitiesInCampToBuffer(caster.camp);
            for (int i = 0; i < TargetSelect.TargetBuffer.Count; i++)
                ApplyConfigBuff(caster, TargetSelect.TargetBuffer[i], 0);
        }
    }
    #endregion
}
