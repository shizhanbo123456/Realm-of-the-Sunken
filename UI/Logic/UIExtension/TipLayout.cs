using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 悬浮提示摆位：跟在鼠标右下角，越界翻到另一侧，始终和鼠标留出间隙
    // 边界取屏幕：提示可以画到所在面板之外，只要不超出屏幕
    internal static class TipLayout
    {
        private const float Gap = 16f;

        public static void PlaceAtPointer(GComponent tip)
        {
            var root = tip.parent as GComponent;
            if (root == null) return; // 还没挂到页面上，等调用方加完子节点再摆
            Vector2 pointer = root.GlobalToLocal(Stage.inst.touchPosition);
            Vector2 min = root.GlobalToLocal(Vector2.zero);
            Vector2 max = root.GlobalToLocal(new Vector2(GRoot.inst.width, GRoot.inst.height));
            float x = pointer.x + Gap;
            if (x + tip.width > max.x) x = pointer.x - Gap - tip.width;
            float y = pointer.y + Gap;
            if (y + tip.height > max.y) y = pointer.y - Gap - tip.height;
            x = Mathf.Clamp(x, min.x, Mathf.Max(min.x, max.x - tip.width));
            y = Mathf.Clamp(y, min.y, Mathf.Max(min.y, max.y - tip.height));
            tip.SetXY(x, y);
            tip.visible = true;
        }
    }
}
