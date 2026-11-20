using UnityEngine;

namespace TerrainGen
{
    public enum PointKind
    {
        MainGuard,
        SubGuard,
        NeutralStatue,
        AttackSpawn,
        DefenseSpawn,
        Tower
    }

    public class MapPoint
    {
        public readonly Vector2Int Cell;
        public PointKind Kind { get; private set; }

        public MapPoint(Vector2Int cell, PointKind kind)
        {
            Cell = cell;
            Kind = kind;
        }

        // 普通点位先生成再定型：攻守出生点与中立雕像由到主守护点的距离决定
        public void SetKind(PointKind kind) => Kind = kind;

        public string DisplayName
        {
            get
            {
                switch (Kind)
                {
                    case PointKind.MainGuard: return "Guard_Main";
                    case PointKind.SubGuard: return "Guard_Sub";
                    case PointKind.NeutralStatue: return "Statue";
                    case PointKind.AttackSpawn: return "Spawn_Attack";
                    case PointKind.DefenseSpawn: return "Spawn_Defense";
                    case PointKind.Tower: return "Tower";
                    default: return "Open";
                }
            }
        }
    }
}
