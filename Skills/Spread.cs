using System;
using Ros.Skill.Utils;
using UnityEngine;

namespace Ros.Skill
{
    // 扇形齐射：N 发 × M 波，双端共用同一套展开角度与波次时序，每发把轨迹交给 emit
    public static class Spread
    {
        public static void Fan(Vector3 origin, Vector3 aim, int shots, int waves, float spreadDeg, float waveGap,
            float bulletLife, Action<BulletTrajectory> emit,
            Func<int, int> shotsOfWave = null, Func<int, float> spreadOfWave = null)
        {
            if (waves <= 0) throw new ArgumentOutOfRangeException(nameof(waves), waves, "波数必须 > 0");
            if (shots <= 0) throw new ArgumentOutOfRangeException(nameof(shots), shots, "发数必须 > 0");
            for (int wave = 0; wave < waves; wave++)
            {
                int w = wave;
                if (w == 0) Wave(origin, aim, shots, spreadDeg, bulletLife, emit, shotsOfWave, spreadOfWave, w);
                else Timer.AddTimer(w, _ => Wave(origin, aim, shots, spreadDeg, bulletLife, emit, shotsOfWave, spreadOfWave, w),
                    waveGap * w, 1, false);
            }
        }

        // 单波：发数 > 1 走扇形展开；单发时 spread 表示整体偏转（扫射靠逐波改这个值实现）
        private static void Wave(Vector3 origin, Vector3 aim, int shots, float spreadDeg, float bulletLife,
            Action<BulletTrajectory> emit, Func<int, int> shotsOfWave, Func<int, float> spreadOfWave, int wave)
        {
            int count = Mathf.Max(1, shotsOfWave == null ? shots : shotsOfWave(wave));
            float spread = spreadOfWave == null ? spreadDeg : spreadOfWave(wave);
            if (count > 1)
            {
                Vector3[] dests = SpreadStyle.FanDests(origin, aim, count, spread);
                for (int i = 0; i < count; i++) emit(new SkillTrajectoryLine(origin, dests[i], bulletLife));
                return;
            }
            Vector3 dest = Mathf.Abs(spread) < 0.01f ? aim : SpreadStyle.RotateTargetAroundDirection(origin, aim, spread);
            emit(new SkillTrajectoryLine(origin, dest, bulletLife));
        }

        // 机枪扫射：朝同一落点连续发射，每发在 ±spreadDeg 内取一个随机偏角（距离不变），按 interval 连发
        public static void MachineGun(Vector3 origin, Vector3 aim, int seed, float spreadDeg, int shots, float interval,
            float bulletLife, Action<BulletTrajectory> emit)
        {
            if (shots <= 0) throw new ArgumentOutOfRangeException(nameof(shots), shots, "发数必须 > 0");
            if (interval < 0f) throw new ArgumentOutOfRangeException(nameof(interval), interval, "连发间隔不能为负");
            if (spreadDeg < 0f) throw new ArgumentOutOfRangeException(nameof(spreadDeg), spreadDeg, "散布角不能为负");

            // 随机序列在施放时一次取完：后续回调被丢弃也不会让两端错位
            var angles = new float[shots];
            var rng = SkillSeed.Rng(seed);
            for (int i = 0; i < shots; i++) angles[i] = ((float)rng.NextDouble() * 2f - 1f) * spreadDeg;

            for (int shot = 0; shot < shots; shot++)
            {
                int s = shot;
                if (s == 0) Fire(origin, aim, angles[s], bulletLife, emit);
                else Timer.AddTimer(s, _ => Fire(origin, aim, angles[s], bulletLife, emit), interval * s, 1, false);
            }
        }

        private static void Fire(Vector3 origin, Vector3 aim, float angleDeg, float bulletLife, Action<BulletTrajectory> emit)
        {
            Vector3 dest = Mathf.Abs(angleDeg) < 0.01f ? aim : SpreadStyle.RotateTargetAroundDirection(origin, aim, angleDeg);
            emit(new SkillTrajectoryLine(origin, dest, bulletLife));
        }
    }
}
