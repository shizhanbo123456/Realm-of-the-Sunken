using FairyGUI;

namespace Ros.UI.Main
{
    // 悬浮提示统一入口：条目只报 id，面板创建/挂载/摆位/锚点失效收起都在这里
    internal static class TipHost
    {
        private const float CheckInterval = 0.1f;

        private static UI_SkillTip skillTip;
        private static UI_BuffTip buffTip;
        private static GObject anchor;
        private static readonly TimerCallback check = CheckAnchor;

        public static void ShowSkill(int skillId, GObject from)
        {
            HideBuffTip();
            if (skillTip == null || skillTip.isDisposed) skillTip = UI_SkillTip.CreateInstance();
            Attach(skillTip, from);
            skillTip.Fill(skillId);
            skillTip.ShowAt(from);
            Watch(from);
        }

        public static void ShowBuff(int buffType, GObject from)
        {
            HideSkillTip();
            if (buffTip == null || buffTip.isDisposed) buffTip = UI_BuffTip.CreateInstance();
            Attach(buffTip, from);
            buffTip.Fill(buffType);
            buffTip.ShowAt(from);
            Watch(from);
        }

        public static void Hide()
        {
            HideSkillTip();
            HideBuffTip();
            anchor = null;
            Timers.inst.Remove(check);
        }

        private static void HideSkillTip()
        {
            if (skillTip != null && !skillTip.isDisposed) skillTip.Hide();
        }

        private static void HideBuffTip()
        {
            if (buffTip != null && !buffTip.isDisposed) buffTip.Hide();
        }

        // 挂到锚点所在页面的根节点：跟着页面显隐/销毁，也不会被列表裁剪
        private static void Attach(GComponent tip, GObject from)
        {
            var root = PageRoot(from);
            if (tip.parent == root) return;
            if (tip.parent != null) tip.RemoveFromParent();
            root.AddChild(tip);
        }

        // 页面根 = 锚点往上第一个挂在 GRoot 下的组件（提示层要压在页面所有元素之上）
        private static GComponent PageRoot(GObject from)
        {
            for (GObject p = from != null ? from.parent : null; p != null; p = p.parent)
                if (p is GComponent root && (root.parent == null || root.parent is GRoot)) return root;
            return GRoot.inst;
        }

        private static void Watch(GObject from)
        {
            anchor = from;
            if (!Timers.inst.Exists(check)) Timers.inst.Add(CheckInterval, 0, check);
        }

        // 轮询锚点：列表回收、血条隐藏、面板关闭都会让锚点失效
        private static void CheckAnchor(object param)
        {
            if (IsAlive(anchor)) return;
            Hide();
        }

        private static bool IsAlive(GObject o)
        {
            if (o == null || o.isDisposed || !o.onStage || !o.visible) return false;
            for (GObject p = o.parent; p != null; p = p.parent)
                if (!p.visible) return false;
            return true;
        }
    }
}
