using System.Collections.Generic;
using UnityEngine;

namespace TerrainGen
{
    // 取预制体的包围盒（根节点空间，已含根缩放）：优先根物体 BoxCollider，缺失时用 Mesh 兜底
    public static class PrefabBounds
    {
        public static bool TryGet(GameObject prefab, Dictionary<GameObject, Bounds> cache, out Bounds bounds)
        {
            if (cache != null && cache.TryGetValue(prefab, out bounds)) return true;

            Bounds raw;
            BoxCollider box = prefab.GetComponent<BoxCollider>();
            if (box != null)
            {
                raw = new Bounds(box.center, box.size);
            }
            else if (!FromMesh(prefab, out raw))
            {
                Debug.LogWarning($"[地形] 预制体 {prefab.name} 既没有根物体 BoxCollider，也没有可用的 Mesh，已跳过");
                bounds = default;
                return false;
            }
            else
            {
                Debug.LogWarning($"[地形] 预制体 {prefab.name} 根物体没有 BoxCollider，已用 Mesh 包围盒兜底，建议补一个 BoxCollider");
            }

            Vector3 sc = prefab.transform.localScale;
            var absSc = new Vector3(Mathf.Abs(sc.x), Mathf.Abs(sc.y), Mathf.Abs(sc.z));
            bounds = new Bounds(Vector3.Scale(raw.center, sc), Vector3.Scale(raw.size, absSc));
            if (cache != null) cache[prefab] = bounds;
            return true;
        }

        // 只按 Mesh 算（不看 BoxCollider），供编辑器预处理工具用
        public static bool TryGetMeshBounds(GameObject prefab, out Bounds bounds)
        {
            return FromMesh(prefab, out bounds);
        }

        static bool FromMesh(GameObject prefab, out Bounds bounds)
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
