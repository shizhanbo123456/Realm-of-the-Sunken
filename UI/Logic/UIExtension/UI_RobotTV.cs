using System;
using System.Collections.Generic;
using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 电视机助理：表情、台词、节奏全部来自 assistant_expression.csv，这里只负责按帧切控制器档位与填文本
    public partial class UI_RobotTV
    {
        private const int TriggerEnter = 0;    // 进入游戏
        private const int TriggerIdleAnim = 1; // 待机动画
        private const int TriggerPress = 2;    // 按钮
        private const int TriggerIdleHold = 3; // 待机
        private const float EnterDelay = 2f;   // 进入游戏后延迟播放开场动画

        private enum Stage
        {
            Enter,
            IdleHold,
            IdleAnim,
            Press,
        }

        private class Clip
        {
            public readonly List<ConfigManager.AssistantExpressionConfig> frames = new();
            public int weight;
        }

        private readonly Dictionary<long, Clip> clipMap = new();
        private readonly Dictionary<int, List<Clip>> clipsByTrigger = new();

        private Clip current;
        private Stage stage;
        private int frameIndex;
        private float frameTimer;
        private bool constructed;
        private bool pendingEnter;
        private float enterDelayTimer;

        // 创建后调用一次：聚合配表、接管点击、播开场
        public void Construct()
        {
            BuildClips();
            onClick.Add(OnTapped);
            constructed = true;
            ClearText(); // 进入游戏立即清掉设计期残留文本，开场动画延迟期间保持干净
            pendingEnter = true;
            enterDelayTimer = EnterDelay;
        }

        public void Shutdown()
        {
            if (!constructed) return;
            onClick.Remove(OnTapped);
            constructed = false;
            current = null;
            pendingEnter = false;
        }

        public void Tick(float dt)
        {
            if (!constructed) return;

            if (pendingEnter)
            {
                enterDelayTimer -= dt;
                if (enterDelayTimer <= 0f)
                {
                    pendingEnter = false;
                    Play(Pick(TriggerEnter), Stage.Enter);
                }
                return;
            }

            if (current == null) return;

            frameTimer -= dt;
            if (frameTimer > 0f) return;
            AdvanceFrame();
        }

        private void OnTapped()
        {
            if (!constructed) return;
            Play(Pick(TriggerPress), Stage.Press);
        }

        private static long ClipKey(int trigger, int clipId) => ((long)trigger << 32) | (uint)clipId;

        private void BuildClips()
        {
            clipMap.Clear();
            clipsByTrigger.Clear();

            foreach (ConfigManager.AssistantExpressionConfig row in ConfigManager.AssistantExpressionConfig.content)
            {
                long key = ClipKey(row.trigger, row.clipId);
                if (!clipMap.TryGetValue(key, out Clip clip))
                {
                    clip = new Clip();
                    clipMap.Add(key, clip);

                    if (!clipsByTrigger.TryGetValue(row.trigger, out List<Clip> list))
                    {
                        list = new List<Clip>();
                        clipsByTrigger.Add(row.trigger, list);
                    }
                    list.Add(clip);
                }

                if (row.frameDuration <= 0f)
                    throw new FormatException($"[助理表情] 触发事件 {row.trigger} 动画 {row.clipId} 的每帧时长必须大于 0（第 {row.seq} 条）");
                if (row.image < 0 || row.image >= m_graphic.m_c1.pageCount)
                    throw new FormatException($"[助理表情] 第 {row.seq} 条的图片档位 {row.image} 超出控制器 m_c1 的 0~{m_graphic.m_c1.pageCount - 1} 页");

                // 权重只认该动画第一个非零值；后续帧重复填同一个权重属冗余，忽略
                if (clip.weight == 0) clip.weight = row.weight;

                clip.frames.Add(row);
            }

            foreach (KeyValuePair<int, List<Clip>> pair in clipsByTrigger)
            {
                foreach (Clip clip in pair.Value)
                {
                    if (clip.weight > 0) continue;
                    ConfigManager.AssistantExpressionConfig head = clip.frames[0];
                    throw new FormatException($"[助理表情] 触发事件 {pair.Key} 动画 {head.clipId} 没有随机权重（第 {head.seq} 条应为该动画首行）");
                }
            }

            RequireTrigger(TriggerEnter);
            RequireTrigger(TriggerIdleAnim);
            RequireTrigger(TriggerPress);
            RequireTrigger(TriggerIdleHold);
        }

        private void RequireTrigger(int trigger)
        {
            if (clipsByTrigger.TryGetValue(trigger, out List<Clip> list) && list.Count > 0) return;
            throw new FormatException($"[助理表情] assistant_expression.csv 缺少触发事件 {trigger} 的数据");
        }

        private Clip Pick(int trigger)
        {
            List<Clip> list = clipsByTrigger[trigger];

            int total = 0;
            for (int i = 0; i < list.Count; i++) total += list[i].weight;

            int roll = UnityEngine.Random.Range(0, total);
            for (int i = 0; i < list.Count; i++)
            {
                roll -= list[i].weight;
                if (roll < 0) return list[i];
            }

            return list[list.Count - 1];
        }

        private void Play(Clip clip, Stage nextStage)
        {
            current = clip;
            stage = nextStage;
            frameIndex = -1;
            AdvanceFrame();
        }

        private void AdvanceFrame()
        {
            frameIndex++;
            if (frameIndex >= current.frames.Count)
            {
                FinishClip();
                return;
            }

            ConfigManager.AssistantExpressionConfig frame = current.frames[frameIndex];
            m_graphic.m_c1.selectedIndex = frame.image;
            ShowText(frame.text);
            frameTimer = frame.frameDuration;
        }

        // 进入游戏 / 待机动画 / 按钮 播完都回待机；待机播完再随机挑一个待机动画，如此循环
        private void FinishClip()
        {
            current = null;

            switch (stage)
            {
                case Stage.Enter:
                case Stage.Press:
                case Stage.IdleAnim:
                    Play(Pick(TriggerIdleHold), Stage.IdleHold);
                    break;
                case Stage.IdleHold:
                    Play(Pick(TriggerIdleAnim), Stage.IdleAnim);
                    break;
            }
        }

        // 进入游戏时立即清掉设计期残留文本：清空内容并隐藏，等开场动画首帧有文本再顶出
        private void ClearText()
        {
            m_label.text = string.Empty;
            m_label.alpha = 0f;
        }

        // 空文本不动上一条：让上一条的转场自己播完；有文本则立即顶掉并重播转场
        private void ShowText(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            m_label.text = text;
            m_label.alpha = 1f;
            m_t0.Play();
        }
    }
}
