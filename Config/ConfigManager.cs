using UnityEngine;

namespace Ros.Config
{
    // 配表管理中心：Awake 一次性加载全部表，并烘培各表包装层的映射数据
    [DefaultExecutionOrder(-200)]
    public partial class ConfigManager : MonoBehaviour
    {
        public static ConfigManager Instance { get; private set; }
        public static bool Loaded { get; private set; }

        private void Awake()
        {
            Instance = this;
            Tool.ConfigManager = this;
            Init();
        }

        public void Init()
        {
            if (Loaded) return;
            LoadAll();
            InitExtensions();
            Loaded = true;
            Debug.Log("[配表] 加载完成 | " + Summary());
        }

        // 编辑器工具在非播放态也要读配表（Awake 不会跑）：优先复用场景里已有的 ConfigManager，
        // 没有才建一个不参与保存的临时宿主
        public static ConfigManager EnsureLoaded()
        {
            if (Instance == null)
            {
                ConfigManager scene = FindObjectOfType<ConfigManager>();
                if (scene != null)
                {
                    Instance = scene;
                    Tool.ConfigManager = scene;
                }
                else
                {
                    var host = new GameObject("[ConfigManager]") { hideFlags = HideFlags.HideAndDontSave };
                    Instance = host.AddComponent<ConfigManager>();
                }
            }
            Instance.Init();
            return Instance;
        }

        private void InitExtensions()
        {
            EntityConfig.Bake(this);
            SkillConfig.Bake(this);
            EffectConfig.Bake(this);
            WeaponConfig.Bake(this);
            AttrRangeConfig.Bake(this);
            CharacterExpConfig.Bake(this);
            CardOptionConfig.Bake(this);
            CardGainUpgradeConfig.Bake(this);
            BattleFuryLayerConfig.Bake(this);
            ReviveTimeConfig.Bake(this);
            PromptConfig.Bake(this);
            SystemMessageConfig.Bake(this);
            MessageVariable.Bake(this);
            QuickMessageConfig.Bake(this);
            SettingTipsConfig.Bake(this);
            AssistantExpressionConfig.Bake(this);
            GlobalVariable.Bake(this);
            PageConfig.Bake(this);
            TerrainGeneration.Bake(this);
            GmCommandConfig.Bake(this);
            AiCommandConfig.Bake(this);
        }

        private string Summary()
        {
            return "entity " + entityData.Count
                 + " | skill " + skillData.Count
                 + " | effect " + effectData.Count
                 + " | global_variables " + global_variablesData.Count
                 + " | terrain_generation " + terrain_generationData.Count;
        }
    }
}
