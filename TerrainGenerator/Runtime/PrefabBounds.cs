using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 生成期只认根物体 BoxCollider，O(1) 取 center/size，绝不遍历网格
    public static class PrefabBounds
    {
        public static bool TryGet(GameObject prefab, Dictionary<GameObject, Bounds> cache, out Bounds bounds)
        {
            if (cache != null && cache.TryGetValue(prefab, out bounds)) return true;

            BoxCollider box = prefab.GetComponent<BoxCollider>();
            if (box == null)
            {
                Debug.LogWarning($"[地形] 预制体 {prefab.name} 根物体没有 BoxCollider，已跳过（先跑 模块化地形/碰撞体预处理）");
                bounds = default;
                return false;
            }

            Vector3 sc = prefab.transform.localScale;
            var absSc = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
            bounds = new Bounds(Vector3.Scale(box.center, sc), Vector3.Scale(box.size, absSc));
            if (cache != null) cache[prefab] = bounds;
            return true;
        }

        // 仅供编辑器“碰撞体预处理”使用：按 Mesh 算包围盒去生成 BoxCollider，生成期不调用
        public static bool TryGetMeshBounds(GameObject prefab, out Bounds bounds)
        {
            bounds = default;
            bool has = false;
            Transform root = prefab.transform;

            foreach (MeshFilter mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Add(ref bounds, ref has, mf.sharedMesh.bounds, root, mf.transform);
            }
            foreach (SkinnedMeshRenderer smr in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (smr.sharedMesh == null) continue;
                Add(ref bounds, ref has, smr.sharedMesh.bounds, root, smr.transform);
            }
            return has;
        }

        static void Add(ref Bounds bounds, ref bool has, Bounds local, Transform root, Transform t)
        {
            Matrix4x4 m = ToRoot(t, root);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3(
                    (i & 1) == 0 ? local.min.x : local.max.x,
                    (i & 2) == 0 ? local.min.y : local.max.y,
                    (i & 4) == 0 ? local.min.z : local.max.z);
                Vector3 p = m.MultiplyPoint3x4(corner);
                if (!has)
                {
                    bounds = new Bounds(p, Vector3.zero);
                    has = true;
                }
                else bounds.Encapsulate(p);
            }
        }

        // 手工累乘 local TRS，避免依赖预制体资产是否已进入场景
        static Matrix4x4 ToRoot(Transform t, Transform root)
        {
            Matrix4x4 m = Matrix4x4.identity;
            Transform cur = t;
            while (cur != null && cur != root)
            {
                m = Matrix4x4.TRS(cur.localPosition, cur.localRotation, cur.localScale) * m;
                cur = cur.parent;
            }
            return m;
        }
    }
}
