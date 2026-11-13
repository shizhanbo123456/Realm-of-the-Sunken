using FairyGUI;
using Ros.Transport;
using UnityEngine;

namespace Ros.UI.Main
{
    // 快捷指令点位：目标点投影到屏幕后夹进距边 100 像素的安全区，被夹住时用箭头指真实方向
    public partial class UI_QuickMessagePointer
    {
        private const float ScreenMargin = 100f;  // 距屏幕四边的最小像素
        private const float ClampEpsilon = 0.5f;  // 判定「是否被夹住」的像素容差（亚像素误差不算夹住）

        private SCQuickMessage message; // 原始包：hasTargetPosition 决定锚点来源
        private float expireAt;
        private Vector3 lastAnchor;     // 发送者实体取不到时的最后已知位置

        public int SenderClientId => message.senderClientId;

        // 创建后调用一次；duration 取自 quick_message.csv 的「点位显示时长」
        public void Bind(SCQuickMessage quickMessage, float duration)
        {
            message = quickMessage;
            expireAt = Time.unscaledTime + duration;
            m_graphic.m_type.selectedIndex = quickMessage.messageId; // 档位即指令 id
            if (m_arrow != null) m_arrow.visible = false;
            touchable = false; // 纯显示，别挡住贴边处的点击
            visible = true;
        }

        public bool Expired(float now) => now >= expireAt;

        // senderPos / hasSenderPos：随包没带位置时改用发送者实体当前位置（实体消失就停在最后已知位置）
        public void Place(Vector3 senderPos, bool hasSenderPos, float uiScale)
        {
            var cam = WorldLayer.ProjectionCamera;
            if (cam == null)
            {
                visible = false;
                return;
            }

            Vector3 anchor = message.hasTargetPosition
                ? message.targetPosition
                : hasSenderPos ? senderPos : lastAnchor;
            lastAnchor = anchor;

            Vector3 projected = cam.WorldToScreenPoint(anchor);
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 target = new Vector2(projected.x, projected.y);
            // 相机背后的点投影会绕屏幕中心翻到另一侧，镜像回来才能保证转视角时方向连续
            if (projected.z <= 0f) target = center * 2f - target;

            // 边距按「整块图标」算：安全区再内缩半个图标，保证图标本身离屏幕边至少 100 像素
            float marginX = ScreenMargin + width * 0.5f * uiScale;
            float marginY = ScreenMargin + height * 0.5f * uiScale;
            Vector2 placed = new Vector2(
                ClampAxis(target.x, marginX, Screen.width - marginX, center.x),
                ClampAxis(target.y, marginY, Screen.height - marginY, center.y));

            visible = true;
            xy = WorldLayer.ToLayer(this, new Vector2(placed.x / uiScale, (Screen.height - placed.y) / uiScale));

            bool clamped = (placed - target).sqrMagnitude > ClampEpsilon * ClampEpsilon;
            PlaceArrow(clamped, target - placed);
        }

        public void DestroySelf()
        {
            if (parent != null) parent.RemoveChild(this);
            Dispose();
        }

        // 安全区退化（屏幕太小，两侧边距叠加超过屏宽）时直接落屏幕正中
        private static float ClampAxis(float value, float min, float max, float center)
            => max < min ? center : Mathf.Clamp(value, min, max);

        // 箭头默认朝上、FGUI 旋转为顺时针，所以角度 = atan2(横向, 纵向)，纵向取屏幕 y 向上
        private void PlaceArrow(bool clamped, Vector2 direction)
        {
            if (m_arrow == null) return;
            m_arrow.visible = clamped;
            if (!clamped) return;

            float angle = Mathf.Atan2(direction.x, direction.y) * Mathf.Rad2Deg;
            m_arrow.rotation = angle < 0f ? angle + 360f : angle;
        }
    }
}
