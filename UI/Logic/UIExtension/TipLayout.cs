using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 悬浮提示摆位：跟在鼠标右下角，越界翻到另一侧，始终和鼠标留出间隙
    internal static class TipLayout
    {
        private const float Gap = 16f;

        public static void PlaceAtPointer(GComponent tip)
        {
            var root = tip.parent as GComponent;
            if (root == null) return; // 还没挂到页面上，等调用方加完子节点再摆
            Vector2 p = root.GlobalToLocal(Stage.inst.touchPosition);
            float x = p.x + Gap;
            if (x + tip.width > root.width) x = p.x - Gap - tip.width;
            float y = p.y + Gap;
            if (y + tip.height > root.height) y = p.y - Gap - tip.height;
            x = Mathf.Clamp(x, 0f, Mathf.Max(0f, root.width - tip.width));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, root.height - tip.height));
            tip.SetXY(x, y);
            tip.visible = true;
        }
    }
}
