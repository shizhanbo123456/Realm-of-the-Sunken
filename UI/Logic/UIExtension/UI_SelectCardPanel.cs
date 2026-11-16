using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

using Ros.Config;
namespace Ros.UI.Main
{
    // 抽卡界面：三张卡横向铺开、逐张播入场动画；选卡 / 弃卡 / 关闭三个出口都由调用方决定怎么上报
    // 选中后是否收起由调用方决定：同一档还有次数时直接就地换下一组，面板不关、不重复淡入
    public partial class UI_SelectCardPanel
    {
        private const float CardShowStagger = 0.08f; // 逐张入场的间隔（秒）
        private const float PanelFadeDuration = 0.12f;

        private readonly List<CardRoll> rolls = new();
        private Action<CardRoll> onSelect;
        private Action onDiscard;
        private Action onClose;
        private CardTier openedTier;
        private GTweener fadeTween;

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
            bool wasVisible = visible;
            Fill(cards, tier, luck);
            if (wasVisible) return; // 选完还有次数就地换下一组：面板已可见，不再重新淡入

            fadeTween?.Kill();
            alpha = 0f;
            fadeTween = GTween.To(0f, 1f, PanelFadeDuration).OnUpdate(t => alpha = t.value.x);
        }

        private void Fill(List<CardRoll> cards, CardTier tier, int luck)
        {
            rolls.Clear();
            if (cards != null) rolls.AddRange(cards);
            openedTier = tier;

            IsOpen = true;
            visible = true;
            touchable = true;
            if (m_discard_description != null) m_discard_description.text = $"弃卡换经验 +{DiscardExp(tier)}";
            if (m_current_luck != null) m_current_luck.text = $"幸运 {luck}";

            // 条目数不变时列表不会重渲染，先清空再铺，保证每组卡都从池里新建并重播入场
            if (m_list != null)
            {
                m_list.itemRenderer = RenderCard;
                m_list.numItems = 0;
                m_list.numItems = rolls.Count;
            }
        }

        // 关闭：不播退场/淡出动画，直接收起（选卡频率高，动画会拖战斗节奏）
        public void Close()
        {
            IsOpen = false;
            HideImmediately();
        }

        // 立即收起（进/出战斗重置用）
        public void CloseImmediately() => Close();

        private void HideImmediately()
        {
            fadeTween?.Kill();
            fadeTween = null;
            visible = false;
            touchable = false;
            alpha = 1f;
            rolls.Clear();
            if (m_list != null) m_list.numItems = 0;
        }

        // 弃卡后收面板还是就地换下一组，由调用方决定
        private void Discard()
        {
            if (!IsOpen) return;
            var action = onDiscard;
            if (action == null)
            {
                Close();
                return;
            }
            action.Invoke();
        }

        // 关闭不清空待抽次数：按钮留在战斗界面上，想好了再点
        private void CloseRequested()
        {
            if (!IsOpen) return;
            var action = onClose;
            Close();
            action?.Invoke();
        }

        // 选中后收面板还是就地换下一组，由调用方决定（这里不再自行关闭）
        private void Select(CardRoll roll)
        {
            if (!IsOpen) return;
            var action = onSelect;
            if (action == null)
            {
                Close();
                return;
            }
            action.Invoke(roll);
        }

        private void RenderCard(int index, GObject obj)
        {
            if (obj is not UI_Card card || index >= rolls.Count) return;
            var roll = rolls[index];
            // 卡名/描述/选卡按钮/技能图标都挂在卡面（UI_CardGraphic）上，图层由卡面统一控制
            var face = card.m_cardGraphic;
            if (face == null) return;

            face.m_level.selectedIndex = (int)roll.tier;
            if (face.m_title != null) face.m_title.text = roll.title;
            if (face.m_des != null) face.m_des.text = roll.description;
            if (face.m_show_skill_display != null) face.m_show_skill_display.selectedIndex = roll.IsSkill ? 1 : 0;

            Texture icon = roll.IsSkill ? UI_SkillList.ResolveIcon(roll.skillId) : null;
            if (face.m_skillpreview != null && face.m_skillpreview.m_loader_icon != null)
            {
                face.m_skillpreview.m_loader_icon.texture = icon != null ? new NTexture(icon) : null;
                face.m_skillpreview.m_loader_icon.visible = icon != null;
            }

            if (face.m_btn_select != null) face.m_btn_select.onClick.Set(() => Select(roll));
            if (card.m_show != null) card.m_show.Play(1, index * CardShowStagger, null);
        }

        // 弃卡折算的经验值与服务器口径一致（同一份配表）
        private static int DiscardExp(CardTier tier)
        {
            return tier switch
            {
                CardTier.Silver => GlobalVariable.I(global_variablesEnum.card_discard_exp_silver),
                CardTier.Gold => GlobalVariable.I(global_variablesEnum.card_discard_exp_gold),
                _ => GlobalVariable.I(global_variablesEnum.card_discard_exp_holo),
            };
        }
    }
}
