using System;
using UnityEditor;
using UnityEngine;

namespace TerrainGen.EditorTools
{
    // 从正上方用正交相机把整张地图渲染一遍，得到每一列的顶面高度，供全屏焦散做遮挡判定
    public static class CausticsOcclusionBaker
    {
        public const int Resolution = 2048;
        public const string AssetPath = "Assets/Resources/Effects/Caustics/CausticsOcclusion.asset";
        public const string ResourcePath = "Effects/Caustics/CausticsOcclusion";

        const string ShaderName = "Hidden/Caustics/HeightBake";
        const string FolderPath = "Assets/Resources/Effects/Caustics";
        const string EffectsFolder = "Assets/Resources/Effects";
        const string ResourcesFolder = "Assets/Resources";

        // 相机清屏用的哨兵值：远低于任何真实高度即可，运行时判空靠的是「超出真实高度范围」而不是这个具体数值
        const float NoGeometryValue = -10000f;

        // root 必须是 SpawnColliderOnly(stripMeshes: false) 铺出来的完整地形
        public static bool Bake(Transform root, float groundHeight, out string message)
        {
            try
            {
                return BakeInternal(root, groundHeight, out message);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                message = "烘焙失败：异常 " + e.GetType().Name + " - " + e.Message;
                return false;
            }
        }

        static bool BakeInternal(Transform root, float groundHeight, out string message)
        {
            float mapMin = TerrainSpawns.MapMin;
            float mapSize = TerrainSpawns.MapSize;
            if (mapSize <= 0f)
            {
                message = "烘焙失败：配表 terrain_size 无效（<=0），无法确定世界范围";
                return false;
            }

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                message = $"烘焙失败：找不到 shader「{ShaderName}」（检查 Assets/Files/Shaders/Caustics/CausticsHeightBake.shader 是否编译通过）";
                return false;
            }

            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                message = "烘焙失败：临时地形里没有任何 Renderer";
                return false;
            }

            float maxY = groundHeight;
            float minY = groundHeight;
            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds bounds = renderers[i].bounds;
                if (bounds.max.y > maxY) maxY = bounds.max.y;
                if (bounds.min.y < minY) minY = bounds.min.y;
            }

            RenderTexture rt = null;
            GameObject camGo = null;
            Texture2D readback = null;
            Debug.Log($"[焦散遮挡图] 开始烘焙：mapMin={mapMin:0.#} mapSize={mapSize:0.#} 高度 {minY:0.##}~{maxY:0.##}m 物件 {renderers.Length} 个");
            try
            {
                rt = new RenderTexture(Resolution, Resolution, 24, RenderTextureFormat.RFloat, RenderTextureReadWrite.Linear);
                rt.Create();

                camGo = new GameObject("CausticsHeightBakeCamera");
                Camera cam = camGo.AddComponent<Camera>();
                cam.orthographic = true;
                cam.orthographicSize = mapSize * 0.5f;
                cam.transform.position = new Vector3(mapMin + mapSize * 0.5f, maxY + 2f, mapMin + mapSize * 0.5f);
                cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // 俯视：屏幕 x→世界X，屏幕 y→世界Z
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = maxY + 2f - minY + 2f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(NoGeometryValue, 0f, 0f, 1f); // 空列用哨兵值，避免和"几何在 Y<0"混淆
                cam.cullingMask = ~0;
                cam.allowHDR = false;
                cam.allowMSAA = false;
                cam.depthTextureMode = DepthTextureMode.None;
                cam.targetTexture = rt;
                cam.SetReplacementShader(shader, "");

                cam.Render();

                readback = new Texture2D(Resolution, Resolution, TextureFormat.RFloat, false, true);
                RenderTexture.active = rt;
                readback.ReadPixels(new Rect(0, 0, Resolution, Resolution), 0, 0);
                readback.Apply();
                RenderTexture.active = null;
            }
            finally
            {
                if (camGo != null) UnityEngine.Object.DestroyImmediate(camGo);
                if (rt != null)
                {
                    rt.Release();
                    UnityEngine.Object.DestroyImmediate(rt);
                }
            }

            // RFloat → 半浮点；清屏色会被 Linear 色彩空间做 gamma→线性转换（-10000 变 -773.99），
            // 所以不能用等值比较判断空列，改为落在渲染前真实高度范围之外的像素一律视为空列
            var raw = readback.GetRawTextureData<float>();
            var half = new ushort[raw.Length];
            float readMin = float.MaxValue;
            float readMax = float.MinValue;
            int emptyCount = 0;
            for (int i = 0; i < raw.Length; i++)
            {
                float v = raw[i];
                if (v < readMin) readMin = v;
                if (v > readMax) readMax = v;
                if (v < minY - 1f || v > maxY + 1f)
                {
                    v = groundHeight;
                    emptyCount++;
                }
                half[i] = Mathf.FloatToHalf(v);
            }

            // 渲染没生效时（替换 shader 没编译 / 相机没渲染到东西）回读全是 0 或纯色，这里直接判定失败
            if (readMax <= minY + 0.5f)
            {
                UnityEngine.Object.DestroyImmediate(readback);
                message = $"烘焙失败：回读高度只有 {readMin:0.##}~{readMax:0.##}m，替换 shader「{ShaderName}」可能没生效";
                return false;
            }
            UnityEngine.Object.DestroyImmediate(readback);
            Debug.Log($"[焦散遮挡图] 回读高度 {readMin:0.##}~{readMax:0.##}m（场景高度 {minY:0.##}~{maxY:0.##}m），空列 {emptyCount} 个按地面高度填充");

            // RHalf 存世界高度：R16 是归一化整数，塞半浮点位模式会被读成 值/65535
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RHalf, false, true)
            {
                name = "CausticsOcclusion",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixelData(half, 0);
            texture.Apply(false, false);

            if (!AssetDatabase.IsValidFolder(FolderPath))
            {
                if (!AssetDatabase.IsValidFolder(ResourcesFolder)) AssetDatabase.CreateFolder("Assets", "Resources");
                if (!AssetDatabase.IsValidFolder(EffectsFolder)) AssetDatabase.CreateFolder(ResourcesFolder, "Effects");
                AssetDatabase.CreateFolder(EffectsFolder, "Caustics");
            }

            AssetDatabase.DeleteAsset(AssetPath);
            AssetDatabase.CreateAsset(texture, AssetPath);
            AssetDatabase.SaveAssets();

            message = $"焦散遮挡图已烘焙（俯视渲染）：{Resolution}×{Resolution}，覆盖 {mapSize:0.#}m，高度 {minY:0.##}~{maxY:0.##}m，参与渲染物件 {renderers.Length} 个";
            return true;
        }
    }
}
