using System.Collections.Generic;
using FairyGUI;
using Ros.Config;
using Ros.Gm;
using Ros.Transport;
using Ros.UI.Main;
using UnityEngine;

// GM 面板：菜单与参数全部来自 gm_command 配表；每条命令渲染「主栏(描述) + 副栏(参数) + 按钮」，无参数时不出副栏
public class GmPanel : PageBase
{
    private readonly UI_GmPanel panel;

    private readonly List<string> groups = new();   // 一级菜单：配表 commandName（显示名取 menuName）
    private readonly List<int> subIds = new();      // 二级菜单：当前组下的配表主键（显示名取 subMenuName）
    private string curGroup;
    private int curId = -1;

    public GmPanel(UI_GmPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    public override pageEnum PageKey => pageEnum.gm;

    public bool IsOpen => IsVisible;

    protected override void Init()
    {
        panel.m_firstMenu.m_button_list.itemRenderer = RenderGroupButton;
        panel.m_firstMenu.m_button_list.onClickItem.Add(OnClickGroup);
        panel.m_secondMenu.m_button_list.itemRenderer = RenderSubButton;
        panel.m_secondMenu.m_button_list.onClickItem.Add(OnClickSub);
        panel.m_GmItemList.itemRenderer = RenderItem;
        panel.m_panel.m_close.onClick.Add(CloseSelf);

        // EventManager 是静态的，订阅必须与解绑成对
        EventManager.AddEvent(ClientEvent.OnConnect, OnNetChanged);
        EventManager.AddEvent(ClientEvent.OnExitWorld, OnNetChanged);
        EventManager.AddEvent(ClientEvent.OnRestartGame, OnNetChanged);
        EventManager.AddEvent<int>(ClientEvent.OnBattleStart, OnBattleStartChanged);
        EventManager.AddEvent<SCGm>(ClientEvent.OnGmResult, OnGmResult);
    }

    public override void Shutdown()
    {
        EventManager.RemoveEvent(ClientEvent.OnConnect, OnNetChanged);
        EventManager.RemoveEvent(ClientEvent.OnExitWorld, OnNetChanged);
        EventManager.RemoveEvent(ClientEvent.OnRestartGame, OnNetChanged);
        EventManager.RemoveEvent<int>(ClientEvent.OnBattleStart, OnBattleStartChanged);
        EventManager.RemoveEvent<SCGm>(ClientEvent.OnGmResult, OnGmResult);
    }

    // 打开由 InputManager 的三连 V 触发：每次打开都重建菜单并清空输出
    public override void Enter(ShowParam param)
    {
        Output(string.Empty);
        BuildGroups();
        base.Enter(param);
    }

    private static void CloseSelf()
    {
        if (Tool.UIManager != null) Tool.UIManager.HidePage(pageEnum.gm);
    }

    // 开战：相位由组队期变对局中，各指令的可用时机随之变化 → 重建菜单
    private void OnBattleStartChanged(int camp) => OnNetChanged();

    private void OnNetChanged() { if (IsVisible) BuildGroups(); }

    // 输出框每次覆盖、不追加；非空文本同时进聊天系统栏
    private void Output(string text)
    {
        panel.m_logOutput.text = text;
        if (text.Length == 0) return;
        if (Tool.UIManager != null) Tool.UIManager.Chat.AppendSystem(text);
    }

    #region//菜单
    // numItems 置相同值不会重刷，先置 0 再置 N
    private static void Refresh(GList list, int count)
    {
        list.numItems = 0;
        list.numItems = count;
    }

    private void BuildGroups()
    {
        groups.Clear();
        foreach (var row in GmCommandConfig.ordered)
        {
            if (!GmCommandRunner.Available(row)) continue;
            if (!groups.Contains(row.commandName)) groups.Add(row.commandName);
        }
        Refresh(panel.m_firstMenu.m_button_list, groups.Count);
        if (groups.Count > 0) SelectGroup(groups[0]);
        else { subIds.Clear(); curId = -1; ClearItems(); }
    }

    private void SelectGroup(string group)
    {
        curGroup = group;
        Refresh(panel.m_firstMenu.m_button_list, groups.Count); // 重刷一级菜单，否则上一个组的高亮会残留
        subIds.Clear();
        foreach (var row in GmCommandConfig.ordered)
        {
            if (row.commandName != group || !GmCommandRunner.Available(row)) continue;
            subIds.Add(row.key);
        }
        Refresh(panel.m_secondMenu.m_button_list, subIds.Count);
        if (subIds.Count > 0) SelectSub(0);
        else { curId = -1; ClearItems(); }
    }

    private void SelectSub(int index)
    {
        if (index < 0 || index >= subIds.Count) { curId = -1; ClearItems(); return; }
        curId = subIds[index];
        Refresh(panel.m_secondMenu.m_button_list, subIds.Count); // 重刷二级菜单，否则上一个子项的高亮会残留
        var row = GmCommandConfig.Get(curId);
        int itemCount = 2;
        if (row.paramType.HasValue) itemCount = 3;
        Refresh(panel.m_GmItemList, itemCount);
    }

    private void ClearItems() => Refresh(panel.m_GmItemList, 0);

    private void RenderGroupButton(int index, GObject obj)
    {
        if (index >= groups.Count || obj is not GButton btn) return;
        btn.title = GmCommandConfig.GroupName(groups[index]);
        btn.selected = groups[index] == curGroup;
    }

    private void RenderSubButton(int index, GObject obj)
    {
        if (index >= subIds.Count || obj is not GButton btn) return;
        btn.title = GmCommandConfig.Get(subIds[index]).subMenuName;
        btn.selected = index == subIds.IndexOf(curId);
    }

    private void OnClickGroup(EventContext ctx) => SelectGroup(groups[panel.m_firstMenu.m_button_list.GetChildIndex((GObject)ctx.data)]);
    private void OnClickSub(EventContext ctx) => SelectSub(panel.m_secondMenu.m_button_list.GetChildIndex((GObject)ctx.data));
    #endregion

    #region//条目：主栏 / 副栏 / 按钮
    private void RenderItem(int index, GObject obj)
    {
        if (obj is not UI_GmItem item || curId < 0) return;
        var row = GmCommandConfig.Get(curId);

        if (index == 0)
        {
            item.m_type.selectedPage = "主栏";
            item.m_label_main_column.text = row.des;
            return;
        }

        if (index == 1 && row.paramType.HasValue)
        {
            item.m_type.selectedPage = "副栏";
            item.m_content.selectedPage = ContentPage(row.paramType.Value);
            item.m_label_sub_column.text = row.paramDec;
            item.m_input.text = string.Empty;
            return;
        }

        // 按钮：标题用 FGUI 设计稿里的文案，这里不覆盖
        item.m_type.selectedPage = "按钮";
        item.m_btn.onClick.Clear(); // GList 复用 item，回调先清再挂
        item.m_btn.onClick.Add(Execute);
    }

    private static string ContentPage(int paramType) => paramType switch
    {
        GmCommandRunner.ParamInt => "int",
        GmCommandRunner.ParamBool => "bool",
        GmCommandRunner.ParamFloat => "float",
        GmCommandRunner.ParamVector2 => "v2",
        GmCommandRunner.ParamVector3 => "v3",
        _ => "int",
    };

    private UI_GmItem ItemAt(int index) => panel.m_GmItemList.GetChildAt(index) as UI_GmItem;
    #endregion

    #region//执行
    private void Execute()
    {
        if (curId < 0 || !GmCommandConfig.content.TryGetValue(curId, out var row)) return;

        string raw = string.Empty;
        if (row.paramType.HasValue && ItemAt(1) is UI_GmItem input) raw = input.m_input.text;
        if (!GmCommandRunner.TryParseParam(row, raw, out var arg, out string error))
        {
            string msg = $"{row.subMenuName} 参数解析失败：{error}（输入：{raw}）";
            Output(msg);
            Debug.LogWarning("[GM] " + msg);
            return;
        }

        GmCommandRunner.Run(row, arg, Output); // 输出框与聊天系统栏同时收到结果
    }
    #endregion

    #region//服务器查询回包
    private void OnGmResult(SCGm gm)
    {
        string text = gm.type switch
        {
            GmCommandId.ServerFps => $"服务器帧率：{gm.i} FPS",
            GmCommandId.ServerEntityCount => $"服务器实体数量：{gm.i}",
            GmCommandId.ServerTime => $"服务器时间：{gm.v0.ToString("0.0")}",
            GmCommandId.ServerDayLength => $"白天时长：{gm.v0.ToString("0.0")} s",
            GmCommandId.ServerNightLength => $"夜晚时长：{gm.v0.ToString("0.0")} s",
            GmCommandId.ServerZombieSpawnLevel => $"僵尸刷新等级：{gm.i}",
            _ => null,
        };
        if (text != null) Output(text);
    }
    #endregion
}
