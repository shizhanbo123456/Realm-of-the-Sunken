using UnityEngine;

using Ros.Config;
public class InfoManager : MonoBehaviour
{
    private void Awake()
    {
        Tool.InfoManager = this;
    }

    public int entity_layer;

    public int ground_layer;

    [Tooltip("伤害飘字缩放曲线：横轴为归一化生命周期（0 刚出现 → 1 消失），纵轴为缩放倍率（乘在距离缩放上）")]
    public AnimationCurve damage_label_scale = AnimationCurve.Linear(0f, 1f, 1f, 1f);

    [Tooltip("伤害飘字透明度曲线：横轴为归一化生命周期（0 刚出现 → 1 消失），纵轴为不透明度（0 全透明 → 1 不透明）")]
    public AnimationCurve damage_label_alpha =
        new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0f));

    #region 聊天配色（ChatItem 正文按 UBB 拼接：频道名 + 发送者 + 内容）
    [Tooltip("频道标签（[全频道] / [阵营] / [附近] / [系统]）的文字颜色")]
    public Color chat_channel_color = new Color(0.66f, 0.66f, 0.66f, 1f);

    [Tooltip("进攻方玩家名的文字颜色")]
    public Color chat_name_attack_color = new Color(0.98f, 0.38f, 0.42f, 1f);

    [Tooltip("防守方玩家名的文字颜色")]
    public Color chat_name_defense_color = new Color(0.23f, 0.66f, 1f, 1f);

    [Tooltip("观战方玩家名的文字颜色")]
    public Color chat_name_spectate_color = new Color(1f, 0.88f, 0.25f, 1f);

    [Tooltip("聊天正文的文字颜色")]
    public Color chat_content_color = Color.white;

    public string ChatChannelColorHex => ColorUtility.ToHtmlStringRGB(chat_channel_color);

    public string ChatContentColorHex => ColorUtility.ToHtmlStringRGB(chat_content_color);

    // 发送者名颜色只按玩家阵营三分类取，中立不属于玩家阵营
    public string ChatNameColorHex(int camp)
    {
        switch (camp)
        {
            case (int)PlayerCampType.Attack: return ColorUtility.ToHtmlStringRGB(chat_name_attack_color);
            case (int)PlayerCampType.Defense: return ColorUtility.ToHtmlStringRGB(chat_name_defense_color);
            case (int)PlayerCampType.Spectator: return ColorUtility.ToHtmlStringRGB(chat_name_spectate_color);
            default: throw new System.ArgumentOutOfRangeException(nameof(camp), camp, "聊天发送者阵营只有 0 进攻 / 1 防守 / 2 观战");
        }
    }
    #endregion

    #region 属性（数据源：StreamingAssets/ConfigData/entity.csv）
    public entity GetEntityConfig(EntityType type)
    {
        return EntityConfig.GetByCategory(type);
    }

    // 全局角色下标：0~进攻方末尾 是进攻方，之后是防守方
    public entity GetPlayerCharacterInfo(int globalIndex)
    {
        if (globalIndex < EntityConfig.AttackCharacterCount)
            return EntityConfig.Get(EntityType.Attack(globalIndex));

        return EntityConfig.Get(EntityType.Defense(globalIndex - EntityConfig.AttackCharacterCount));
    }
    #endregion
}
