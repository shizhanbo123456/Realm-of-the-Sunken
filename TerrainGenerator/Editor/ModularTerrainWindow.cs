using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

using Ros.Config;
namespace TerrainGen.EditorTools
{
    public class ModularTerrainWindow : EditorWindow
    {
        const string ConfigFolder = "Assets/Files/TerrainGeneratorConfig";
        const int WalkableArea = 0;      // 项目 NavMeshAreas 第 0 项 Walkable
        const int NotWalkableArea = 1;   // 第 1 项 Not Walkable：只当障碍，不生成行走面

        BuildingSettings settings;
        BuildingKitSet kit;
        BuildingField field;
        bool dirty;
        bool showSettings = true;
        float minRegionAreaM2; // 0 = 用项目 NavMeshAreas 的设置；>0 时覆盖 minRegionArea
        Texture2D preview;
        Vector2 scroll;
        string summary;
        bool summaryIsError;
        readonly List<string> report = new List<string>();

        const string FloorColorKey = "TerrainGen.MapFloorColor";
        const string VoidColorKey = "TerrainGen.MapVoidColor";
        Color mapFloorColor = new Color(0.72f, 0.68f, 0.60f, 1f);
        Color mapVoidColor = new Color(0.09f, 0.10f, 0.14f, 1f);

        [MenuItem("Tools/模块化地形/建筑生成器")]
        public static void Open()
        {
            var window = GetWindow<ModularTerrainWindow>("建筑生成器");
            window.minSize = new Vector2(420f, 400f);
            if (window.position.width < 460f || window.position.height < 520f)
                window.position = new Rect(window.position.x, window.position.y, 480f, 680f);
        }

        void OnEnable()
        {
            settings = AssetDatabase.LoadAssetAtPath<BuildingSettings>(ConfigFolder + "/BuildingSettings.asset");
            kit = AssetDatabase.LoadAssetAtPath<BuildingKitSet>(ConfigFolder + "/BuildingKitSet.asset");
            mapFloorColor = LoadColor(FloorColorKey, mapFloorColor);
            mapVoidColor = LoadColor(VoidColorKey, mapVoidColor);
        }

        void OnDisable() => DestroyImmediate(preview);

        void OnGUI()
        {
            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                settings = (BuildingSettings)EditorGUILayout.ObjectField("生成参数", settings, typeof(BuildingSettings), false);
                if (GUILayout.Button("创建配置", GUILayout.Width(70))) CreateAssets();
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                kit = (BuildingKitSet)EditorGUILayout.ObjectField("素材表", kit, typeof(BuildingKitSet), false);
                if (GUILayout.Button("新建素材表", GUILayout.Width(80))) CreateKit();
            }

            if (settings == null)
            {
                EditorGUILayout.HelpBox("点“创建配置”新建一份建筑生成参数，或把已有资产拖进来。", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("预览布局", GUILayout.Height(24))) Rebuild();
                if (GUILayout.Button("烘焙数据", GUILayout.Height(24))) GenerateIntoScene();
                if (GUILayout.Button("清空场景", GUILayout.Height(24))) ClearScene();
                if (GUILayout.Button("随机种子", GUILayout.Height(24), GUILayout.Width(72))) SetRandomSeed();
            }

            if (!string.IsNullOrEmpty(summary))
                EditorGUILayout.HelpBox(summary, summaryIsError ? MessageType.Error : MessageType.Info);

            EditorGUILayout.Space(4);
            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                mapFloorColor = EditorGUILayout.ColorField("底图地面色", mapFloorColor);
                mapVoidColor = EditorGUILayout.ColorField("底图虚空色", mapVoidColor);
            }
            if (EditorGUI.EndChangeCheck()) SaveMapColors();

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                minRegionAreaM2 = EditorGUILayout.FloatField("最小区域面积(m²)", minRegionAreaM2);
                if (GUILayout.Button("烘焙 NavMesh（要求空场景，按真实网格生成）", GUILayout.Height(24))) BakeNavMesh();
            }
            EditorGUILayout.LabelField("0 = 用项目 NavMeshAreas 里的 minRegionArea；大于 0 时按该面积换算成体素² 覆盖",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("烘焙焦散遮挡图（要求空场景，按真实网格生成）", GUILayout.Height(24))) BakeCausticsOcclusion();
            }
            EditorGUILayout.LabelField("产物 Assets/Resources/Effects/Caustics/CausticsOcclusion.asset，供相机上的 ScreenSpaceCaustics 判断屋顶下不铺焦散",
                EditorStyles.wordWrappedMiniLabel);

            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));

            showSettings = EditorGUILayout.Foldout(showSettings, "生成参数", true);
            if (showSettings)
            {
                var so = new SerializedObject(settings);
                so.Update();
                var it = so.GetIterator();
                while (it.NextVisible(true))
                {
                    if (it.name == "m_Script") continue;
                    EditorGUILayout.PropertyField(it, true);
                }
                if (so.ApplyModifiedProperties()) dirty = true;
            }

            if (preview != null)
            {
                EditorGUILayout.Space(4);
                Rect rect = GUILayoutUtility.GetRect(260f, 260f, GUILayout.ExpandWidth(false));
                EditorGUI.DrawRect(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), new Color(0.35f, 0.35f, 0.35f));
                EditorGUI.DrawPreviewTexture(rect, preview, null, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("输出", EditorStyles.boldLabel);
            for (int i = 0; i < report.Count; i++)
                EditorGUILayout.LabelField(report[i], EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndScrollView();
        }

        void Log(string msg)
        {
            if (report.Count >= 300) report.RemoveRange(0, 150);
            report.Add(msg);
        }

        void SetRandomSeed()
        {
            var so = new SerializedObject(settings);
            so.Update();
            so.FindProperty("seed").intValue = Random.Range(1, int.MaxValue);
            so.ApplyModifiedProperties();
            dirty = true;
        }

        void Rebuild()
        {
            report.Clear();
            if (!EnsureConfig(out string configError))
            {
                summary = "预览失败：" + configError;
                summaryIsError = true;
                Log(summary);
                return;
            }

            field = new BuildingField();
            field.Generate(settings);
            dirty = false;
            BuildPreviewTexture();

            int sumW = 0;
            int sumH = 0;
            foreach (RectInt room in field.Rooms)
            {
                sumW += room.width;
                sumH += room.height;
            }
            float cell = TerrainGeneration.F(terrain_generationEnum.cell_size);
            float avgW = field.Rooms.Count > 0 ? sumW * cell / field.Rooms.Count : 0f;
            float avgH = field.Rooms.Count > 0 ? sumH * cell / field.Rooms.Count : 0f;

            int guards = 0;
            int extras = 0;
            int towers = 0;
            foreach (MapPoint p in field.Points)
            {
                if (p.Kind == PointKind.Extra) extras++;
                else if (p.Kind == PointKind.Tower) towers++;
                else guards++;
            }

            summary = $"布局已生成：{field.Rooms.Count} 个房间（平均 {avgW:0}×{avgH:0} 单位），地板 {field.FloorCellCount()} 格，"
                      + $"点位 {field.Points.Count} 个（守护 {guards} / 普通 {extras} / 防御塔 {towers}）。";
            summaryIsError = field.FloorCellCount() == 0;
            Log(summary);
        }

        void BuildPreviewTexture()
        {
            DestroyImmediate(preview);
            preview = new Texture2D(field.SizeX, field.SizeZ, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point
            };

            var roomColors = new Color[field.Rooms.Count];
            var rng = new System.Random(settings.seed);
            for (int i = 0; i < roomColors.Length; i++)
                roomColors[i] = Color.HSVToRGB((float)rng.NextDouble(), 0.45f, 0.88f);

            for (int x = 0; x < field.SizeX; x++)
            {
                for (int z = 0; z < field.SizeZ; z++)
                {
                    int region = field.Region[x, z];
                    Color c;
                    if (field.Layer[x, z] < 0) c = new Color(0.80f, 0.80f, 0.82f);
                    else if (region < 0) c = new Color(0.55f, 0.53f, 0.50f);
                    else c = roomColors[region % roomColors.Length];
                    preview.SetPixel(x, z, c);
                }
            }
            foreach (MapPoint p in field.Points)
            {
                Color c;
                switch (p.Kind)
                {
                    case PointKind.MainGuard: c = new Color(1f, 0.2f, 0.1f); break;
                    case PointKind.SubGuard: c = new Color(1f, 0.62f, 0.05f); break;
                    case PointKind.Tower: c = new Color(0.25f, 0.85f, 0.3f); break;
                    default: c = new Color(0.1f, 0.7f, 1f); break;
                }
                for (int dx = 0; dx < 2; dx++)
                    for (int dz = 0; dz < 2; dz++)
                    {
                        int px = p.Cell.x + dx;
                        int pz = p.Cell.y + dz;
                        if (px < 0 || pz < 0 || px >= field.SizeX || pz >= field.SizeZ) continue;
                        preview.SetPixel(px, pz, c);
                    }
            }

            preview.Apply();
        }

        void GenerateIntoScene()
        {
            report.Clear();
            if (!EnsureConfig(out string configError))
            {
                summary = "烘焙失败：" + configError;
                summaryIsError = true;
                Log(summary);
                return;
            }

            int cells = TerrainGeneration.I(terrain_generationEnum.map_cells_xz);
            if (field == null || dirty || field.SizeX != cells || field.SizeZ != cells) Rebuild();

            TerrainPlanData data = TerrainGenerator.Bake(settings, kit);
            if (data == null)
            {
                summary = "烘焙失败：无法构建摆放数据，检查生成参数与素材表";
                summaryIsError = true;
                Log(summary);
                return;
            }

            bool mapOk = TerrainMapImage.Write(field, mapFloorColor, mapVoidColor, out string mapPath);
            // 落盘后要等编辑器导入完才拿得到 importer，放到延迟回调里做
            if (mapOk) EditorApplication.delayCall += () =>
            {
                AssetDatabase.Refresh();
                ApplyTextureImport(mapPath);
            };

            summary = $"已烘焙 {data.items.Count} 个物件、{data.points.Count} 个点位（{field.Rooms.Count} 个房间）→ "
                      + TerrainPlanData.PathFor(settings.seed, settings.version)
                      + "；进播放模式后由 Culler 按区块动态生成";
            summary += mapOk
                ? $"\n底图 → {mapPath}（{TerrainMapImage.Resolution}×{TerrainMapImage.Resolution}，只画有无地面）"
                : "\n底图生成失败，小地图将缺少底板";
            summaryIsError = !mapOk;
            Log(summary);
        }

        void SaveMapColors()
        {
            EditorPrefs.SetString(FloorColorKey, "#" + ColorUtility.ToHtmlStringRGBA(mapFloorColor));
            EditorPrefs.SetString(VoidColorKey, "#" + ColorUtility.ToHtmlStringRGBA(mapVoidColor));
        }

        static Color LoadColor(string key, Color fallback)
        {
            string html = EditorPrefs.GetString(key, string.Empty);
            return !string.IsNullOrEmpty(html) && ColorUtility.TryParseHtmlString(html, out Color c) ? c : fallback;
        }

        // 底图是纯色块，压缩或 mipmap 会让格子边缘发糊，导入后固定成未压缩 + 双线性
        static void ApplyTextureImport(string assetPath)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                Debug.LogError($"[地形] 底图导入失败：编辑器里找不到 {assetPath}，检查 Resources/Map 是否被忽略");
                return;
            }
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = TerrainMapImage.Resolution;
            importer.sRGBTexture = true;
            importer.SaveAndReimport();
        }

        // 烘导航数据：先确认场景里没有别的网格/碰撞体，再按服务器那套铺一次地形来烘，烘完清掉
        void BakeNavMesh()
        {
            report.Clear();

            if (Application.isPlaying)
            {
                summary = "烘焙终止：请退出播放模式再烘 NavMesh。";
                summaryIsError = true;
                Log(summary);
                return;
            }

            if (!TryCheckSceneClean(out string blocked))
            {
                summary = "烘焙终止：场景里还有其它网格或碰撞体，先清空场景再烘。\n" + blocked;
                summaryIsError = true;
                Log(summary);
                return;
            }

            if (!EnsureConfig(out string configError))
            {
                summary = "烘焙失败：" + configError;
                summaryIsError = true;
                Log(summary);
                return;
            }

            if (kit == null)
            {
                summary = "烘焙失败：未指定素材表";
                summaryIsError = true;
                Log(summary);
                return;
            }

            TerrainPlanData data = TerrainGenerator.BuildData(settings, kit);
            if (data == null)
            {
                summary = "烘焙失败：无法构建摆放数据，检查生成参数与素材表";
                summaryIsError = true;
                Log(summary);
                return;
            }

            var root = new GameObject("NavMeshBakeRoot");
            root.transform.position = Vector3.zero;

            bool ok;
            string message;
            try
            {
                TerrainAssembler.SpawnColliderOnly(data, kit, root.transform, stripMeshes: false);
                ok = TryBakeNavMesh(root.transform, data, out message);
            }
            finally
            {
                DestroyImmediate(root); // 烘完立刻清掉临时地形，场景回到干净状态
            }

            summary = message;
            summaryIsError = !ok;
            Log(summary);
        }

        // 烘焦散遮挡图：流程同 NavMesh 烘焙，铺满整图后俯视正交渲染一遍真实几何
        void BakeCausticsOcclusion()
        {
            report.Clear();

            if (Application.isPlaying)
            {
                ReportCaustics("烘焙终止：请退出播放模式再烘焦散遮挡图。", true);
                return;
            }

            if (!TryCheckSceneClean(out string blocked))
            {
                ReportCaustics("烘焙终止：场景里还有其它网格或碰撞体，先清空场景再烘。\n" + blocked, true);
                return;
            }

            if (!EnsureConfig(out string configError))
            {
                ReportCaustics("烘焙失败：" + configError, true);
                return;
            }

            if (kit == null)
            {
                ReportCaustics("烘焙失败：未指定素材表", true);
                return;
            }

            TerrainPlanData data = TerrainGenerator.BuildData(settings, kit);
            if (data == null)
            {
                ReportCaustics("烘焙失败：无法构建摆放数据，检查生成参数与素材表", true);
                return;
            }

            var root = new GameObject("CausticsBakeRoot");
            root.transform.position = Vector3.zero;

            bool ok;
            string message;
            try
            {
                TerrainAssembler.SpawnColliderOnly(data, kit, root.transform, stripMeshes: false);
                ok = CausticsOcclusionBaker.Bake(root.transform, data.groundHeight, out message);
            }
            finally
            {
                DestroyImmediate(root); // 烘完立刻清掉临时地形，场景回到干净状态
            }

            ReportCaustics(message, !ok);
        }

        // 焦散烘焙结果只走 Console：面板里的文字既复制不了也不好找
        void ReportCaustics(string msg, bool error)
        {
            summary = msg;
            summaryIsError = error;
            if (error) Debug.LogError(msg);
            else Debug.Log(msg);
        }

        bool TryBakeNavMesh(Transform root, TerrainPlanData data, out string message)
        {
            message = string.Empty;
            float groundY = data.groundHeight;

            NavMeshBuildSettings build = NavMesh.GetSettingsByID(0);
            if (build.agentTypeID < 0)
            {
                message = "烘焙失败：项目里没有 AgentType 0 的导航设置（菜单 Window > AI > Navigation）";
                return false;
            }

            // 项目设置里的 minRegionArea 在 Navigation 面板上没暴露，这里按 m² 换算成体素² 覆盖
            float minRegion = build.minRegionArea;
            if (minRegionAreaM2 > 0f)
            {
                float voxelArea = build.voxelSize * build.voxelSize;
                if (voxelArea <= 0f)
                {
                    message = "烘焙失败：agent 的 voxelSize 读不到（0），无法把 m² 换算成体素²";
                    return false;
                }
                minRegion = Mathf.Max(1f, Mathf.Round(minRegionAreaM2 / voxelArea));
                build.minRegionArea = minRegion;
            }

            // 地面按格子铺整块 Box（素材网格自身有缝也能保证底板连续）；其余物件用真实网格但标成 Not Walkable，
            // 只当障碍、不产生可行走面——否则围栏顶、立柱顶、岩石顶、屋檐全是一块块孤岛。
            // 不用 CollectSources(PhysicsColliders)：编辑器态刚生成的碰撞体还没同步进物理场景，会收错。
            var sources = new List<NavMeshBuildSource>();
            Bounds bounds = default;
            bool hasBounds = false;
            int groundCount = 0;
            int meshCount = 0;
            float cell = data.cellSize;
            var cellSize = new Vector3(cell + 0.1f, 1f, cell + 0.1f); // 略微重叠，保证相邻地块接得上

            foreach (Transform item in root)
            {
                if (item.name.StartsWith("Ground_", System.StringComparison.Ordinal))
                {
                    Vector3 center = new Vector3(item.position.x, groundY - 0.5f, item.position.z);
                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Box,
                        size = cellSize,
                        area = WalkableArea,
                        transform = Matrix4x4.TRS(center, Quaternion.identity, Vector3.one),
                    });

                    var slab = new Bounds(center, cellSize);
                    if (!hasBounds) { bounds = slab; hasBounds = true; }
                    else bounds.Encapsulate(slab);

                    groundCount++;
                    continue;
                }

                foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null) continue;

                    sources.Add(new NavMeshBuildSource
                    {
                        shape = NavMeshBuildSourceShape.Mesh,
                        sourceObject = mesh,
                        area = NotWalkableArea,
                        transform = filter.transform.localToWorldMatrix,
                    });
                    meshCount++;

                    var renderer = filter.GetComponent<MeshRenderer>();
                    if (renderer == null) continue;
                    if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                    else bounds.Encapsulate(renderer.bounds);
                }
            }

            if (sources.Count == 0 || !hasBounds)
            {
                message = "烘焙失败：生成物里没有可用的网格，检查素材表与碰撞体预处理";
                return false;
            }

            bounds.Expand(2f); // 留出 agent 半径余量
            // Y 只留「地面 + 一个 agent 高」：再高的几何不参与，省体素也省得烘出高处的面
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            min.y = groundY - 1f;
            max.y = groundY + Mathf.Max(build.agentHeight, 1f);
            bounds.SetMinMax(min, max);

            NavMeshData navData = NavMeshBuilder.BuildNavMeshData(build, sources, bounds, Vector3.zero, Quaternion.identity);
            if (navData == null)
            {
                message = "烘焙失败：BuildNavMeshData 返回空";
                return false;
            }

            string path = SaveNavMeshAsset(navData);
            message = $"NavMesh 已烘焙：地面 {groundCount} 块 + 障碍网格 {meshCount} 个，"
                      + $"范围 X {bounds.min.x:0}~{bounds.max.x:0} / Z {bounds.min.z:0}~{bounds.max.z:0}、Y {bounds.min.y:0.#}~{bounds.max.y:0.#}、"
                      + $"minRegionArea {minRegion:0} 体素²（≈{minRegion * build.voxelSize * build.voxelSize:0.#} m²）→ {path}";
            return true;
        }

        static string SaveNavMeshAsset(NavMeshData data)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
            if (!AssetDatabase.IsValidFolder(TerrainNavMesh.Folder)) AssetDatabase.CreateFolder("Assets/Resources", "Nav");

            string path = TerrainNavMesh.Folder + "/" + TerrainNavMesh.AssetName + ".asset";
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(data, path);
            AssetDatabase.SaveAssets();
            return path;
        }

        // 场景必须干净：任何网格或碰撞体都会污染烘焙结果（含上次运行留下的地形、预览内容）
        static bool TryCheckSceneClean(out string detail)
        {
            var meshes = new List<string>();
            var colliders = new List<string>();

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject sceneRoot in scene.GetRootGameObjects())
                {
                    foreach (MeshFilter filter in sceneRoot.GetComponentsInChildren<MeshFilter>(true)) meshes.Add(filter.name);
                    foreach (SkinnedMeshRenderer skin in sceneRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true)) meshes.Add(skin.name);
                    foreach (Terrain terrain in sceneRoot.GetComponentsInChildren<Terrain>(true)) meshes.Add(terrain.name);
                    foreach (Collider collider in sceneRoot.GetComponentsInChildren<Collider>(true)) colliders.Add(collider.name);
                }
            }

            if (meshes.Count == 0 && colliders.Count == 0)
            {
                detail = string.Empty;
                return true;
            }

            detail = $"场景里还有网格 {meshes.Count} 个、碰撞体 {colliders.Count} 个"
                     + (meshes.Count > 0 ? $"，例如「{meshes[0]}」" : $"，例如「{colliders[0]}」");
            return false;
        }

        // 编辑器非播放态 ConfigManager.Awake 不会跑，配表没加载时静态包装层会直接抛「未加载」
        static bool EnsureConfig(out string error)
        {
            if (!Ros.Config.ConfigManager.Loaded)
            {
                try
                {
                    Ros.Config.ConfigManager.EnsureLoaded();
                }
                catch (System.Exception e)
                {
                    error = "配表加载失败：" + e.Message;
                    return false;
                }
            }

            if (!Ros.Config.ConfigManager.Loaded)
            {
                error = "配表初始化没有完成，terrain_generation 不可用";
                return false;
            }

            error = string.Empty;
            return true;
        }

        void ClearScene()
        {
            report.Clear();
            TerrainPropCuller[] cullers = Object.FindObjectsOfType<TerrainPropCuller>();
            if (cullers.Length == 0)
            {
                // 服务器地形只有内容根、没有 Culler
                GameObject root = GameObject.Find(TerrainAssembler.RootName);
                if (root != null)
                {
                    Undo.DestroyObjectImmediate(root);
                    summary = "已删除 1 个地形内容根（服务器地形没有 Culler）。";
                }
                else summary = "场景里没有地形节点。";
                summaryIsError = false;
                return;
            }
            int removed = 0;
            foreach (TerrainPropCuller c in cullers)
            {
                if (c == null) continue;
                Transform content = c.ContentRoot;
                Undo.DestroyObjectImmediate(c.gameObject);
                if (content != null) Undo.DestroyObjectImmediate(content.gameObject);
                removed++;
            }
            summary = $"已删除 {removed} 个地形节点（Culler 与内容根各一）。";
            summaryIsError = false;
        }

        void CreateKit()
        {
            EnsureConfigFolder();

            string path = ConfigFolder + "/BuildingKitSet.asset";
            kit = AssetDatabase.LoadAssetAtPath<BuildingKitSet>(path);
            if (kit != null)
            {
                summary = "素材表已存在，未覆盖。";
                summaryIsError = false;
                return;
            }

            kit = CreateInstance<BuildingKitSet>();
            AssetDatabase.CreateAsset(kit, path);
            AssetDatabase.SaveAssets();
            summary = "已新建空素材表，请把地面 / 围墙 / 矮石柱 / 高石柱 / 支撑 / 建筑物 / 装饰 / 灌木 / 摆件 / 藤蔓 / 障碍 / 障碍装饰 / 漂浮装饰 / 鱼群预制体拖进十四个列表。";
            summaryIsError = false;
        }

        void CreateAssets()
        {
            EnsureConfigFolder();
            settings = AssetDatabase.LoadAssetAtPath<BuildingSettings>(ConfigFolder + "/BuildingSettings.asset");
            if (settings == null)
            {
                settings = CreateInstance<BuildingSettings>();
                AssetDatabase.CreateAsset(settings, ConfigFolder + "/BuildingSettings.asset");
                AssetDatabase.SaveAssets();
            }
            summary = "生成参数已就绪：" + ConfigFolder + "/BuildingSettings.asset";
            summaryIsError = false;
        }

        // 按 ConfigFolder 逐级建目录，避免创建位置与读取位置不一致
        static void EnsureConfigFolder()
        {
            if (AssetDatabase.IsValidFolder(ConfigFolder)) return;

            string[] parts = ConfigFolder.Split('/');
            string cur = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = cur + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(cur, parts[i]);
                cur = next;
            }
        }
    }
}
