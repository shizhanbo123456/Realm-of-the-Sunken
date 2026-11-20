using System;
using Ros.Skill.Utils;
using UnityEngine;

namespace Ros.Skill
{
    // 扇形齐射：N 发 × M 波，双端共用同一套展开角度与波次时序，每发把轨迹交给 emit
    public static class Spread
    {
        // 一次施放的总发数：与 Fan 的发射口径完全一致（多波累加，发数随波变化时按 shotsOfWave 求和）
        // 技能的伤害倍率按这个数均摊，所以两边必须是同一个口径
        public static int Count(int shots, int waves, Func<int, int> shotsOfWave = null)
        {
            if (waves <= 0) throw new ArgumentOutOfRangeException(nameof(waves), waves, "波数必须 > 0");
            if (shots <= 0) throw new ArgumentOutOfRangeException(nameof(shots), shots, "发数必须 > 0");
            int total = 0;
            for (int wave = 0; wave < waves; wave++)
                total += Mathf.Max(1, shotsOfWave == null ? shots : shotsOfWave(wave));
            return total;
        }

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

        // 蓄力瞄准：子弹散布在发射球面中间带上，驻留 hold 后全部射向同一点
        public static void ChargeAim(Vector3 center, Vector3 aim, int seed, float radius, int shots, float hold,
            float flight, Action<BulletTrajectory> emit)
        {
            ValidateCharge(radius, shots, hold, flight);
            Vector3[] points = ChargePoints(center, seed, radius, shots);
            for (int i = 0; i < shots; i++)
                emit(new SkillTrajectoryHoldLine(points[i], aim, hold, flight));
        }

        // 蓄力扫射：落点 = 目标 + 各自的球面偏移，所有弹道互相平行
        public static void ChargeSweep(Vector3 center, Vector3 aim, int seed, float radius, int shots, float hold,
            float flight, Action<BulletTrajectory> emit)
        {
            ValidateCharge(radius, shots, hold, flight);
            Vector3[] points = ChargePoints(center, seed, radius, shots);
            Vector3 delta = aim - center;
            for (int i = 0; i < shots; i++)
                emit(new SkillTrajectoryHoldLine(points[i], points[i] + delta, hold, flight));
        }

        private static void ValidateCharge(float radius, int shots, float hold, float flight)
        {
            if (shots <= 0) throw new ArgumentOutOfRangeException(nameof(shots), shots, "发数必须 > 0");
            if (radius <= 0f) throw new ArgumentOutOfRangeException(nameof(radius), radius, "球半径必须 > 0");
            if (hold < 0f) throw new ArgumentOutOfRangeException(nameof(hold), hold, "驻留时长不能为负");
            if (flight <= 0f) throw new ArgumentOutOfRangeException(nameof(flight), flight, "飞行时长必须 > 0");
        }

        // 球面均匀取点，只保留 |dy| <= radius/2 的中间带（cos 在 [-0.5,0.5] 上均匀才是面均匀）
        private static Vector3[] ChargePoints(Vector3 center, int seed, float radius, int shots)
        {
            var points = new Vector3[shots];
            var rng = SkillSeed.Rng(seed);
            for (int i = 0; i < shots; i++)
            {
                float cos = ((float)rng.NextDouble() * 2f - 1f) * 0.5f;
                float sin = Mathf.Sqrt(1f - cos * cos);
                float theta = (float)rng.NextDouble() * (Mathf.PI * 2f);
                points[i] = center + new Vector3(Mathf.Cos(theta) * sin, cos, Mathf.Sin(theta) * sin) * radius;
            }
            return points;
        }

        // 正弦扇形扫射：落点按扇形均分（俯视仍是扇面），每发沿高度上下正弦振荡，按打乱的顺序逐发发射
        public static void SineFan(Vector3 origin, Vector3 aim, int seed, float spreadDeg, int shots, float interval,
            float amplitude, float cycles, float bulletLife, Action<BulletTrajectory> emit)
        {
            if (shots <= 0) throw new ArgumentOutOfRangeException(nameof(shots), shots, "发数必须 > 0");
            if (interval < 0f) throw new ArgumentOutOfRangeException(nameof(interval), interval, "发射间隔不能为负");
            if (spreadDeg < 0f) throw new ArgumentOutOfRangeException(nameof(spreadDeg), spreadDeg, "散布角不能为负");
            if (amplitude < 0f) throw new ArgumentOutOfRangeException(nameof(amplitude), amplitude, "振幅不能为负");
            if (cycles <= 0f) throw new ArgumentOutOfRangeException(nameof(cycles), cycles, "周期数必须 > 0");

            Vector3[] dests = SpreadStyle.FanDests(origin, aim, shots, spreadDeg);
            int[] order = Shuffled(seed, shots);
            for (int shot = 0; shot < shots; shot++)
            {
                int index = order[shot];
                // 相位按扇面序号错开：相邻角度的子弹不在同一高度，整体读成一道扫过扇面的波
                var trajectory = new SkillTrajectorySineLine(origin, dests[index], bulletLife, amplitude, cycles,
                    index * Mathf.PI * 2f / shots);
                if (shot == 0) emit(trajectory);
                else Timer.AddTimer(shot, _ => emit(trajectory), interval * shot, 1, false);
            }
        }

        // Fisher-Yates：发射顺序由种子决定，与落点序号解耦
        private static int[] Shuffled(int seed, int count)
        {
            var order = new int[count];
            for (int i = 0; i < count; i++) order[i] = i;
            var rng = SkillSeed.Rng(seed);
            for (int i = count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                int swap = order[i];
                order[i] = order[j];
                order[j] = swap;
            }
            return order;
        }
    }
}
