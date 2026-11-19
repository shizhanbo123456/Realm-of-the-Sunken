using FairyGUI;
using Ros.UI.Main;
using Ros.Config;
using UnityEngine;

// 加载界面：最高的显示层级，盖住所有页面
public class LoadingPage : PageBase
{
    private const int PageCount = 3; // m_page 档位数（0~2）

    private readonly UI_LoadingPanel panel;

    public LoadingPage(UI_LoadingPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.loading;

    // 每次显示随机挑一种页面档位，进度条先收起（等 SetProgress 再显示）
    public override void Enter(ShowParam param)
    {
        panel.m_page.selectedIndex = Random.Range(0, PageCount);
        panel.m_show_bar.selectedIndex = 0;
        base.Enter(param);
    }

    // progress01 = 0~1 完成度，text 显示在进度文本上；设置即展开进度条
    public void SetProgress(float progress01, string text)
    {
        panel.m_show_bar.selectedIndex = 1;
        panel.m_progressbar.value = Mathf.Clamp01(progress01) * panel.m_progressbar.max;
        panel.m_label.text = text;
    }
}
