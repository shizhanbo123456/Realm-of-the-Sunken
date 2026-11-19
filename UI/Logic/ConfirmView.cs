using System;
using FairyGUI;
using Ros.UI.Main;
using Ros.Config;
using UnityEngine;

// 确认弹窗：全屏拦截层已并入 ConfirmPanel 内部，弹窗本体 opaque 吞掉点击。
public class ConfirmView : PageBase
{
    private UI_ConfirmPanel panel;
    private Action onConfirm;
    private Action onCancel;

    public bool IsOpen => IsVisible;

    public ConfirmView(UI_ConfirmPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.confirm;

    protected override void Init()
    {
        panel.opaque = true; // 吞掉点击：底层页面/聊天/GM 面板都不再响应
        panel.touchable = true;

        panel.m_confirm.onClick.Add(OnConfirmClicked);
        panel.m_cancel.onClick.Add(OnCancelClicked);
    }

    public override void Shutdown()
    {
        onConfirm = null;
        onCancel = null;
        base.Shutdown();
    }

    // content = 正文；confirm = 确认回调；cancel = 取消回调（可为空，此时取消按钮只关闭）
    public void Show(string content, Action confirm, Action cancel = null)
    {
        onConfirm = confirm;
        onCancel = cancel;
        if (panel.m_content != null) panel.m_content.text = content;

        if (Tool.UIManager != null) Tool.UIManager.ShowPage(pageEnum.confirm);
    }

    // 隐藏时清掉回调：取消按钮与外部 CloseConfirm 都不该再触发上一次的操作
    public override void Exit()
    {
        onConfirm = null;
        onCancel = null;
        base.Exit();
    }

    #region//Local
    // 先关闭再回调：回调里可能立刻弹下一个窗或切页
    private void OnConfirmClicked()
    {
        var act = onConfirm;
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.confirm);
        if (act != null) act();
    }

    private void OnCancelClicked()
    {
        var act = onCancel;
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.confirm);
        if (act != null) act();
    }
    #endregion
}
