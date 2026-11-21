using FairyGUI;
using UnityEngine;

using Ros.Config;
namespace Ros.UI.Main
{
    // 技能悬浮提示：名称/描述/冷却/库存 + 六项评分
    public partial class UI_SkillTip
    {
        private static readonly message_variablesEnum[] ScoreNames =
        {
            message_variablesEnum.skill_scoreName_output, message_variablesEnum.skill_scoreName_cooldown,
            message_variablesEnum.skill_scoreName_control, message_variablesEnum.skill_scoreName_support,
            message_variablesEnum.skill_scoreName_range, message_variablesEnum.skill_scoreName_attackSpeed,
        };

        public void Fill(int skillId)
        {
            var cfg = SkillConfig.Get(skillId);
            m_SkillName.text = cfg.name;
            m_skillDes.text = cfg.description;
            m_label_cd.text = MessageVariable.F(message_variablesEnum.skill_cdFormat, cfg.cd);
            if (cfg.storeLimit < 0) m_label_store.text = MessageVariable.V(message_variablesEnum.skill_storeUnlimited);
            else m_label_store.text = cfg.storeLimit.ToString();

            var scores = cfg.scores;
            m_score_list.itemRenderer = (i, obj) =>
            {
                if (obj is not UI_TipLevel row) return;
                if (i < ScoreNames.Length) row.m_title.text = MessageVariable.V(ScoreNames[i]);
                else row.m_title.text = "";
                row.m_level.selectedIndex = Mathf.Clamp(scores[i], 0, 4); // 0~4 = D/C/B/A/S
            };
            m_score_list.numItems = scores.Count;
        }

        public void ShowAtPointer() => TipLayout.PlaceAtPointer(this);

        public void Hide() => visible = false;
    }
}
