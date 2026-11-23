using UnityEngine;

namespace TerrainGen
{
    public enum PointKind
    {
        MainGuard,
        SubGuard,
        NeutralStatue,
        Tower
    }

    public class MapPoint
    {
        public readonly Vector2Int Cell;
        public PointKind Kind { get; }

        public MapPoint(Vector2Int cell, PointKind kind)
        {
            Cell = cell;
            Kind = kind;
        }

        public string DisplayName
        {
            get
            {
                switch (Kind)
                {
                    case PointKind.MainGuard: return "Guard_Main";
                    case PointKind.SubGuard: return "Guard_Sub";
                    case PointKind.NeutralStatue: return "Statue";
                    case PointKind.Tower: return "Tower";
                    default: return "Open";
                }
            }
        }
    }
}
