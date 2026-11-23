using Ros.Skill;
using Ros.Transport;
using UnityEngine;

using Ros.Config;
public static class GlobalConfig
{
    #region 角色
    public const int attack_character_count = 6;
    public const int defense_character_count = 6;

    // 实体胶囊碰撞体半径：所有实体统一写死，不随模型体型变化（只有高度按包围盒 + 骨骼动态收紧）
    public const float entity_capsule_radius = 0.22f;

    // 局内等级压制：每级 ±5% 增伤/减伤，等级差最多计 3 级（对所有伤害生效，含 PVE）
    public const float level_suppress_per_level = 0.05f;
    public const int level_suppress_max_diff = 3;

    // 战斗期等级上限 = global_variables，配表加行即生效
    #endregion

    #region 守护雕像（防守目标）
    public const int sage_statue_count = 3;

    // 守护点下标（协议与战斗界面守护点面板一致）：0 主点 / 1~3 副点
    public static EntityType GuardPointType(int index)
    {
        if (index < 0 || index > sage_statue_count)
            throw new System.ArgumentOutOfRangeException(nameof(index), index, "守护点下标只有 0（主点）~ 3（副点）");
        return index == 0 ? EntityType.KingStatue : EntityType.GuardStatue(index - 1);
    }

    // 守护点血量上限与名字都在这张表里（entity.csv「生命」「姓名」列）
    public static entity GuardPointConfig(int index) => EntityConfig.GetByCategory(GuardPointType(index));

    // 血量上限 = 配表生命 × 开局人数倍率（倍率由 SCDefensePointProgress 下发，0 = 守护点还没生成，按配表值占位）
    public static float GuardPointMaxHealth(int index, float healthScale)
    {
        float baseHealth = GuardPointConfig(index).health;
        return healthScale > 0f ? baseHealth * healthScale : baseHealth;
    }

    // 当前血量 → 被打掉的百分比（0~100）；血量为负说明守护点还没生成，按未受损算
    public static int GuardPointDamagedPercent(int index, int health, float healthScale)
    {
        if (health < 0) return 0;
        float max = GuardPointMaxHealth(index, healthScale);
        if (max <= 0f) throw new System.FormatException($"[配表] entity.csv 守护点 {index} 的生命必须大于 0");
        return Mathf.Clamp((int)(100f * (1f - health / max)), 0, 100);
    }
    #endregion

    #region 资源与中立单位
    public const int crystal_statue_count = 3;          // 场景 3 座水晶雕像，value 0~2

    #region 中立雕像（中立争抢单位）
    public const int neutral_statue_variant_count = 2;   // 0 天使雕像 / 1 恶魔雕像
    public const float neutral_statue_first_spawn_delay = 30f;
    public const float neutral_statue_respawn_delay = 60f;
    public const float neutral_statue_angry_health_ratio = 0.5f;    // 生命值低于该比例进入愤怒态
    public const float neutral_statue_angry_interval_ratio = 0.5f;  // 愤怒态施法间隔 = 普通间隔 × 该系数
    #endregion
    #endregion

    #region 僵尸
    public const int zombie_variant_count = 5;
    public const int elite_zombie_variant_count = 1;

    // —— AI 行为（策划案第九章只写了「无目标时游荡 / 发现目标后主动追击 / 索敌随游荡时长变大」，数值均为占位初值）——
    public const float zombie_decide_interval = 0.15f;
    public const float zombie_acquire_range = 12f;
    public const float zombie_attack_range = 2f;
    public const float zombie_roar_range = 10f;
    // 1 倍索敌的夹取基准：视野夹到「16 - 余量」，攻击态退出要比进入距离大，避免在边界反复切状态
    public const float zombie_max_chase_distance = 16f;
    public const float zombie_acquire_margin = 2f;
    // 未锁定目标时索敌倍率按此速度线性增长（1 倍起步，每秒 +0.1，不设上限）
    public const float zombie_acquire_scale_grow = 0.1f;
    public const float zombie_attack_exit_scale = 1.3f;
    public const float zombie_wander_radius = 8f;
    public const float zombie_wander_pause = 2f;
    // 普通鲛人不按距离筛选索敌：半径直接取「不限距离」档位（> skill_target_unlimited_radius 即不限距离）
    public const float zombie_acquire_unlimited = skill_target_unlimited_radius + 1f;
    // 普通鲛人在追击态累计超过该时长仍未进入攻击态：判定卡死，帧末静默销毁
    public const float zombie_chase_timeout = 30f;
    #endregion

    #region 人机玩家 AI（Doc/08；先写代码，测试后再迁配表）
    public const float ai_think_interval = 0.15f;
    public const float ai_search_radius = 10f;
    public const float ai_search_arrive = 0.6f;
    public const float ai_search_pause_min = 1f;
    public const float ai_search_pause_max = 3f;
    // 走/停滞回：靠近用大阈值、停下用小阈值，中间区间保持原状态，避免在边界反复起停
    public const float ai_hysteresis_ratio = 0.8f;
    public const float ai_danger_clear_arrive = 2f;
    public const float ai_chase_limit = 40f;
    // 索敌半径必须比追击上限小一段：两者相等时「刚索到就已超限」会让搜索/追击每拍互踢
    public const float ai_acquire_margin = 5f;
    public const float ai_combat_retreat_ratio = 0.3f;
    public const float ai_escort_retreat_ratio = 0.15f;
    public const float ai_rally_retreat_ratio = 0.2f;
    public const float ai_danger_response_distance = 30f;
    public const float ai_danger_response_ratio = 0.7f;
    public const float ai_evade_distance = 20f;
    public const float ai_evade_duration = 4f;
    public const float ai_danger_clear_radius = 15f;
    public const float ai_danger_clear_timeout = 30f;
    public const float ai_escort_arrive = 5f;
    public const float ai_escort_follow = 8f;
    public const float ai_escort_combat_radius = 15f;
    public const float ai_escort_slot_radius = 2f;
    public const float ai_escort_timeout = 30f;
    public const float ai_rally_arrive = 5f;
    public const float ai_rally_keep = 10f;
    public const float ai_rally_call_radius = 25f;
    public const int ai_guard_check_interval = 1;
    public const float ai_guard_alert_radius = 15f;
    public const float ai_assault_arrive = 5f;
    public const float ai_assault_combat_activity = 25f;
    public const float ai_unreachable_pause = 2f;
    public const float ai_stuck_min_move = 0.5f; // 采样周期内位移低于此值视为走不到目的地
    public const float ai_message_min_interval = 1f;
    // 服务器给进攻方人机派点的周期，以及「打守护点 / 打防御塔」的分界随机值
    public const float ai_assault_order_interval = 60f;
    public const float ai_assault_order_ratio = 0.5f;
    // 派点前置：周围该半径内有敌方角色（含人机）就不派
    public const float ai_assault_order_quiet_radius = 30f;
    #endregion

    #region 昼夜
    // 昼夜时长取配表 global_variables 的 day_duration / night_duration；时间用归一化周期值（[0,2)：0/2 = 午夜，1 = 正午）循环推演
    public const float daynight_sync_interval = 10f;

    // 首页/大厅的固定环境时间：进页面即覆盖，避免把战斗结束那一刻的昼夜带到局外
    public const float home_env_time = 0.7f;
    public const float lobby_env_time = 1.4f;
    #endregion

    #region 视野与小地图（策划案第十五章）
    // 视野、索敌、小地图共用角色属性 EntityAttribute.viewDistance；角色模型在场景里的显示距离另取
    // global_variables 的 entity_character_visiable_radius（远看无意义，不必同步）。
    public const float minimap_sync_interval = 0.5f;
    public const float minimap_lost_duration = 99999f;

    // 客户端小地图点位超时：超过此时间未收到该实体点位包则隐藏（覆盖「离屏残留」与「夜间/致盲停传」两类情况）
    public const float minimap_entity_timeout = 1.5f;
    // 雷达显示半径切换键
    public const KeyCode minimap_radius_key = KeyCode.F;
    // 上帝视角：窗口期内连按 3 次打开（与 GM 面板同一套连按判定），关闭走底框关闭按钮
    public const KeyCode god_view_open_key = KeyCode.G;
    public const int god_view_open_press_count = 3;
    public const float god_view_open_press_window = 1f;
    #endregion

    #region 玩家操作（鼠标 + 键盘：WASD 相对相机移动 / 空格·左Alt 跳跃 / 左 Shift 滑铲 / 左键普攻 / 右键放选中技能 / 滚轮切技能 / Tab 技能选择）
    public const KeyCode jump_key = KeyCode.Space;
    public const KeyCode jump_key_alt = KeyCode.LeftAlt; // 与空格等效，只为按着顺手
    public const KeyCode slide_key = KeyCode.LeftShift;
    public const KeyCode skill_select_key = KeyCode.Tab; // 技能选择面板开关
    public static readonly KeyCode[] quick_message_keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8 }; // 快捷消息面板：1~8 任键开关
    public const KeyCode camera_mode_key = KeyCode.C; // 相机模式切换（自由视角下按 C 回第三人称）
    public const float gm_teleport_height = 200f; // GM 传送的 Y（传送面板只给 x/z）

    // 移动中转动视角超过该角度就补发一次 CSMove，不逐帧上报
    public const float move_yaw_resend_threshold = 3f;

    // 按住 WASD 期间的重发间隔：服务器朝向靠它持续对齐客户端，避免被自动索敌/受击转向带偏
    public const float move_resend_interval = 0.1f;

    // 鼠标转视角灵敏度（度/屏幕像素）
    public const float mouse_yaw_sensitivity = 0.2f;
    public const float mouse_pitch_sensitivity = 0.15f;

    // 飞行升降 / GM 极速倍率来自配表 global_variables
    #endregion

    #region 武器与技能
    #region 武器技能 id 区间（抽卡按品质在这段技能里筛；段长与顺序不可随意调整）
    public const int weapon_skill_id_min = (int)SkillType.SkillFanIceCone;
    public const int weapon_skill_id_max = (int)SkillType.SkillAllyShieldTwentyFive;
    #endregion

    // 攻击动作播放期间的重试节拍：普攻连段按此频率续段，与真人连点普攻同频
    public const float attack_retry_interval = 0.1f;
    #endregion

    #region 结算与得分
    public const float kill_score_factor = 0.1f;
    #endregion

    #region 同步
    public const float entity_sync_interval_fast = 0.02f;
    public const float entity_sync_interval_details = 1f;   // 全量心跳：兼作静止实体的保活，须小于客户端的 3s 视图超时
    public const float entity_sync_pos_epsilon = 0.1f;  // 位姿脏判定：与上次下发位置差超过该值（米）才置脏
    public const float entity_sync_yaw_epsilon = 3f;    // 位姿脏判定：与上次下发朝向差超过该值（度）才置脏
    #endregion

    #region 寻路
    public const int nav_path_fail_limit = 3;        // 连续算路失败次数上限，达到即视为目标不可达
    public const float ai_ignore_unreachable_time = 1f; // 目标不可达后忽略多久，避免放弃后立刻又锁回同一个
    #endregion

    #region 越界处理
    public const float entity_bounds_check_interval_player = 0.2f; // 玩家越界检查间隔
    public const float entity_bounds_check_interval = 5f;          // 其它实体越界检查间隔
    #endregion

    // 人形实体生成时相对地面抬升，脚底与地面完全重合会陷进去
    public const float entity_spawn_rise = 1f;

    #region 通用
    public const int entity_id_max = 30000;
    public const int unarmed_punch = (int)SkillType.SkillPunch;

    // 移动类 Buff 的移速倍率来自配表 global_variables（同一乘区，连乘叠加）

    // 技能索敌阈值（索敌夹角与射程逐技能读 skill.csv，射程同时就是索敌半径）
    public const float skill_target_no_sector = 0.1f;        // 夹角 <= 此值 = 完全不索敌
    public const float skill_target_full_sector = 179.9f;    // 夹角 > 此值 = 不限方向（360°）
    public const float skill_target_unlimited_radius = 9999f; // 射程 > 此值 = 不限距离

    public const float buff_vfx_life_time = 3600f;

    // Buff 的强度系数（每跳伤害 / 属性幅度 / 护盾 / 反伤 / 祝福 / 嘶吼…）在配表 global_variables

    #region 防守方技能参数（占位初值，待策划定稿）
    public const int power_suppress_hits = 3;
    public const float power_suppress_interval = 0.4f;
    public const int power_suppress_vfx_index = 5;   // RangeMagic[5]
    public const float power_suppress_vfx_life = 1f;
    #endregion

    // Buff 持续表现的特效类别/下标在 effect.csv 第 11、12 列（EffectConfig.vfxKind / vfxIndex）
    #endregion

    #region 刚体（可移动单位的权威速度载体：位移效果只产出速度，位置由物理积分）
    public const float rb_drag = 0f;

    public const float rb_angular_drag = 1f;
    #endregion

    #region 地面检测（探针从脚部 +ground_probe_up 向下打 ground_probe_length；数值来自配表 global_variables）
    #endregion

    #region 防守方角色下标（type.value，顺序同策划案第五章与 entity.csv）
    public const int defense_index_count_eye = 0;      // 白眼伯爵
    public const int defense_index_plague_bringer = 1; // 瘟疫使者
    public const int defense_index_masked_pope = 2;    // 蒙面教皇
    public const int defense_index_deer_knight = 3;    // 鹿铠怪人
    public const int defense_index_death_stroller = 5; // 死灵漫步者（4 = 苍白舞者，被动按标记判定，不引用下标）
    #endregion

    #region 出生点（不烘点位：进攻方随机落在地图外围，防守方落主守护点附近）
    public const float attack_spawn_min_ratio = 0.3f; // 距地图中心的下限，×地图边长；无外半径
    public const float defense_spawn_radius = 6f;     // 防守出生点取主守护点该半径内的合法地面
    #endregion

}
