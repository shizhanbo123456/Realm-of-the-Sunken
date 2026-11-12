using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 抽卡界面：三张卡横向铺开、逐张播入场动画；选卡 / 弃卡 / 关闭三个出口都由调用方决定怎么上报
    public partial class UI_SelectCardPanel
    {
        private const float CardShowStagger = 0.08f; // 逐张入场的间隔（秒）
        private const float PanelFadeDuration = 0.12f;

        private readonly List<CardRoll> rolls = new();
        private Action<CardRoll> onSelect;
        private Action onDiscard;
        private Action onClose;
        private CardTier openedTier;
        private int closeToken; // 关闭动画途中又打开时作废旧动画，避免把新面板收掉

        public bool IsOpen { get; private set; }

        // 本次打开的是哪一档（弃卡按这个档位折算）
        public CardTier OpenedTier => openedTier;

        // 构造期一次：三个按钮接回调
        public void Build(Action<CardRoll> select, Action discard, Action close)
        {
            onSelect = select;
            onDiscard = discard;
            onClose = close;
            if (m_btn_close != null) m_btn_close.onClick.Set(CloseRequested);
            if (m_discard != null) m_discard.onClick.Set(Discard);
        }

        public void Open(List<CardRoll> cards, CardTier tier, int luck)
        {
            rolls.Clear();
            if (cards != null) rolls.AddRange(cards);
            openedTier = tier;

            IsOpen = true;
            visible = true;
            touchable = true;
            if (m_discard_description != null) m_discard_description.text = $"弃卡换经验 +{DiscardExp(tier)}";
            if (m_current_luck != null) m_current_luck.text = $"幸运 {luck}";

            if (m_list != null)
            {
                m_list.itemRenderer = RenderCard;
                m_list.numItems = rolls.Count;
            }

            // 面板淡入 + 卡片逐张入场：让"卡牌出现"这件事在快节奏战斗里也能被看见
            closeToken++;
            alpha = 0f;
            GTween.To(0f, 1f, PanelFadeDuration).OnUpdate(t => alpha = t.value.x);
        }

        // 关闭：卡片先播退场动画，面板随后淡出收起
        public void Close()
        {
            IsOpen = false;
            touchable = false;
            if (!visible) return;

            if (m_list != null)
            {
                for (int i = 0; i < m_list.numChildren; i++)
                {
                    if (m_list.GetChildAt(i) is UI_Card card && card.m_hide != null) card.m_hide.Play(1, i * 0.04f, null);
                }
            }
            int token = ++closeToken;
            GTween.To(1f, 0f, PanelFadeDuration).SetDelay(0.10f).OnUpdate(t => alpha = t.value.x)
                .OnComplete(() => { if (token == closeToken) HideImmediately(); });
        }

        // 立即收起（进/出战斗重置用，不播动画）
        public void CloseImmediately()
        {
            IsOpen = false;
            HideImmediately();
        }

        private void HideImmediately()
        {
            visible = false;
            touchable = false;
            alpha = 1f;
            rolls.Clear();
            if (m_list != null) m_list.numItems = 0;
        }

        private void Discard()
        {
            if (!IsOpen) return;
            var action = onDiscard;
            Close();
            action?.Invoke();
        }

        // 关闭不清空待抽次数：按钮留在战斗界面上，想好了再点
        private void CloseRequested()
        {
            if (!IsOpen) return;
            var action = onClose;
            Close();
            action?.Invoke();
        }

        private void Select(CardRoll roll)
        {
            if (!IsOpen) return;
            var action = onSelect;
            Close();
            action?.Invoke(roll);
        }

        private void RenderCard(int index, GObject obj)
        {
            if (obj is not UI_Card card || index >= rolls.Count) return;
            var roll = rolls[index];

            if (card.m_cardGraphic != null) card.m_cardGraphic.m_level.selectedIndex = (int)roll.tier;
            if (card.m_title != null) card.m_title.text = roll.title;
            if (card.m_des != null) card.m_des.text = roll.description;
            if (card.m_show_skill_display != null) card.m_show_skill_display.selectedIndex = roll.IsSkill ? 1 : 0;

            Texture icon = roll.IsSkill ? UI_SkillList.ResolveIcon(roll.skillId) : null;
            if (card.m_skillpreview != null && card.m_skillpreview.m_loader_icon != null)
            {
                card.m_skillpreview.m_loader_icon.texture = icon != null ? new NTexture(icon) : null;
                card.m_skillpreview.m_loader_icon.visible = icon != null;
            }

            if (card.m_btn_select != null) card.m_btn_select.onClick.Set(() => Select(roll));
            if (card.m_show != null) card.m_show.Play(1, index * CardShowStagger, null);
        }

        // 弃卡折算的经验值与服务器口径一致（同一份配表）
        private static int DiscardExp(CardTier tier)
        {
            return tier switch
            {
                CardTier.Silver => ConfigManager.GlobalVariableConfig.card_discard_exp_silver,
                CardTier.Gold => ConfigManager.GlobalVariableConfig.card_discard_exp_gold,
                _ => ConfigManager.GlobalVariableConfig.card_discard_exp_holo,
            };
        }
    }
}
