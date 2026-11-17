using FairyGUI;
using UnityEngine;

namespace Ros.UI.Main
{
    // 快捷指令点位：挂在小地图（雷达 / 放大版）里，图标档位 = 指令 id，被夹到雷达圆边时用箭头指真实方向
    public partial class UI_QuickMessagePointer
    {
        private const float ArrowReach = 54f; // 图标中心到箭头尖端的距离（箭头伸出图标框外 22 像素）

        // 创建后调用一次：档位取自指令 id，纯显示不吃点击
        public void Bind(int messageId)
        {
            m_graphic.m_type.selectedIndex = messageId;
            if (m_arrow != null) m_arrow.visible = false;
            touchable = false;
        }

        // 轴心居中，所以 xy 就是图标中心
        public void Place(float x, float y) => SetXY(x, y);

        // 贴到雷达圆边时留出的半径（含伸出的箭头）：不夹边时箭头是隐藏的，可多占一点空间
        public float EdgeInset => ArrowReach * scale.x;

        // 箭头默认朝上、FGUI 旋转为顺时针，所以角度 = atan2(右, 上)；direction 是 FGUI 局部坐标（y 向下）
        public void PointOut(Vector2 direction)
        {
            if (m_arrow == null) return;
            m_arrow.visible = true;
            float angle = Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;
            m_arrow.rotation = angle < 0f ? angle + 360f : angle;
        }

        public void DestroySelf()
        {
            if (parent != null) parent.RemoveChild(this);
            Dispose();
        }
    }
}
