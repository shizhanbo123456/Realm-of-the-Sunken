using System;
using System.Collections.Generic;
using System.Globalization;
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

    // 参数类型编码，与配表 paramType 一致
    private const int ParamInt = 0;
    private const int ParamBool = 1;
    private const int ParamFloat = 2;
    private const int ParamVector2 = 3;
    private const int ParamVector3 = 4;

    private struct GmArg
    {
        public int i;
        public float v0, v1, v2;
    }

    public GmPanel(UI_GmPanel panel) : base(panel)
    {
        this.panel = panel;
    }

    protected override UiConfig InitConfig() => new UiConfig
    {
        Order = UILayer.Gm,
        ShowBlackMask = false,
        ClickOutsideToClose = false,
    };

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

    // 与页面同规则的整体缩放（设计高度 1080）；面板居中
    public override void OnResize(float width, float height)
    {
        float uiScale = Screen.height / 1080f;
        panel.scale = new Vector2(uiScale, uiScale);
        panel.SetXY(
            (GRoot.inst.width - panel.width * uiScale) * 0.5f,
            (GRoot.inst.height - panel.height * uiScale) * 0.5f);
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
        if (Tool.UIManager != null) Tool.UIManager.HidePage(PageId.Gm);
    }

    // 开战：相位由组队期变对局中，各指令的可用时机随之变化 → 重建菜单
    private void OnBattleStartChanged(int camp) => OnNetChanged();

    private static bool Online => Tool.NetworkManager != null && NetworkManager.CanSendWorldCommand;

    private void OnNetChanged() { if (IsVisible) BuildGroups(); }

    // 输出框每次覆盖，不追加
    private void Output(string text) => panel.m_logOutput.text = text;

    #region//菜单
    // 当前相位对应的配表列
    private static bool Available(gm_command row)
    {
        if (!Online) return row.home ?? false;
        return NetworkManager.BattleRunning ? row.battle ?? false : row.lobby ?? false;
    }

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
            if (!Available(row)) continue;
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
            if (row.commandName != group || !Available(row)) continue;
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
        Refresh(panel.m_GmItemList, row.paramType.HasValue ? 3 : 2);
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
        ParamInt => "int",
        ParamBool => "bool",
        ParamFloat => "float",
        ParamVector2 => "v2",
        ParamVector3 => "v3",
        _ => "int",
    };

    private UI_GmItem ItemAt(int index) => panel.m_GmItemList.GetChildAt(index) as UI_GmItem;
    #endregion

    #region//执行
    private void Execute()
    {
        if (curId < 0 || !GmCommandConfig.content.TryGetValue(curId, out var row)) return;

        string raw = row.paramType.HasValue && ItemAt(1) is UI_GmItem input ? input.m_input.text : string.Empty;
        if (!TryParseParam(row, raw, out GmArg arg, out string error))
        {
            string msg = $"{row.subMenuName} 参数解析失败：{error}（输入：{raw}）";
            Output(msg);
            Debug.LogWarning("[GM] " + msg);
            return;
        }

        if (RunLocal(row, arg)) return;

        if (!Online) { Output($"{row.subMenuName}：需要联网执行"); return; }
        Tool.NetworkManager.SendGm(new CSGm { type = row.key, i = arg.i, v0 = arg.v0, v1 = arg.v1, v2 = arg.v2 });
    }

    // 参数解析：int/bool/float 直接读，Vector2/3 用 "(1.1,2.2)" 形式
    private static bool TryParseParam(gm_command row, string raw, out GmArg arg, out string error)
    {
        arg = default;
        error = null;
        if (!row.paramType.HasValue) return true;

        string text = (raw ?? string.Empty).Trim();
        if (text.Length == 0) { error = "未填参数"; return false; }

        switch (row.paramType.Value)
        {
            case ParamInt:
                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) { arg.i = i; return true; }
                error = "需要整数";
                return false;

            case ParamBool:
                if (TryParseBool(text, out bool b)) { arg.i = b ? 1 : 0; return true; }
                error = "需要 1/0（也接受 true/false、t/f）";
                return false;

            case ParamFloat:
                if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) { arg.v0 = f; return true; }
                error = "需要小数";
                return false;

            case ParamVector2:
                if (TryParseVector(text, 2, out float[] v2)) { arg.v0 = v2[0]; arg.v1 = v2[1]; return true; }
                error = "需要 (x,y) 两个分量";
                return false;

            case ParamVector3:
                if (TryParseVector(text, 3, out float[] v3)) { arg.v0 = v3[0]; arg.v1 = v3[1]; arg.v2 = v3[2]; return true; }
                error = "需要 (x,y,z) 三个分量";
                return false;

            default:
                error = $"配表 paramType={row.paramType.Value} 不认识（只支持 0~4）";
                return false;
        }
    }

    private static bool TryParseBool(string text, out bool value)
    {
        switch (text.ToLowerInvariant())
        {
            case "1": case "t": case "true": value = true; return true;
            case "0": case "f": case "false": value = false; return true;
            default: value = false; return false;
        }
    }

    private static bool TryParseVector(string text, int count, out float[] values)
    {
        values = null;
        string s = text.Trim();
        if (s.StartsWith("(")) s = s.Substring(1);
        if (s.EndsWith(")")) s = s.Substring(0, s.Length - 1);
        s = s.Replace('，', ',');
        string[] parts = s.Split(',');
        if (parts.Length != count) return false;

        float[] result = new float[count];
        for (int k = 0; k < count; k++)
        {
            if (!float.TryParse(parts[k].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result[k])) return false;
        }
        values = result;
        return true;
    }
    #endregion

    #region//本地指令（不联网也生效；返回 true 表示已处理）
    private bool RunLocal(gm_command row, GmArg arg)
    {
        switch (row.key)
        {
            case GmCommandId.ShowEnemyCampConversation:
                ChatPlugin.ShowEnemyDialog = arg.i != 0;
                Output($"敌方阵营聊天：{(ChatPlugin.ShowEnemyDialog ? "开" : "关")}");
                return true;

            case GmCommandId.SettlementView:
                if (Tool.UIManager != null) Tool.UIManager.Settlement.PlayDemo();
                Output("已播放结算界面演示");
                return true;

            case GmCommandId.Delay:
                if (arg.i < 0) { Output("延迟不能为负"); return true; }
                if (Tool.Instance != null) Tool.Instance.delay = arg.i;
                Output($"本地延迟已设为 {arg.i} ms");
                return true;

            case GmCommandId.DebugFps:
                Output($"{(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f)).ToString("0")} FPS");
                return true;

            case GmCommandId.DebugEntityCount:
                Output($"客户端实体数量：{CountEntities()}");
                return true;

            case GmCommandId.ClientTime:
                Output($"客户端时间：{Time.time.ToString("0.0")}");
                return true;

            case GmCommandId.MeshSurfaceCount:
                Output($"模型网格面数：{CountSceneMeshFaces()}");
                return true;

            case GmCommandId.Fog:
                var env = Tool.EnvironmentManager;
                if (env == null) { Output("雾气：当前场景没有 EnvironmentManager"); return true; }
                env.SetFogEnabled(arg.i != 0);
                Output($"雾气：{(env.fogEnabled ? "开" : "关")}");
                return true;

            default:
                return false;
        }
    }

    private static int CountEntities()
    {
        var players = Tool.ClientLogicManager != null ? Tool.ClientLogicManager.EntityPlayers : null;
        if (players == null) return 0;
        int count = 0;
        foreach (var _ in players.AllViews) count++;
        return count;
    }

    // 模型网格面数：遍历场景中所有网格物体累加三角面
    private static long CountSceneMeshFaces()
    {
        long faces = 0;
        foreach (var mf in UnityEngine.Object.FindObjectsOfType<MeshFilter>())
        {
            var mesh = mf.sharedMesh;
            if (mesh != null) faces += mesh.triangles.Length / 3L;
        }
        foreach (var smr in UnityEngine.Object.FindObjectsOfType<SkinnedMeshRenderer>())
        {
            var mesh = smr.sharedMesh;
            if (mesh != null) faces += mesh.triangles.Length / 3L;
        }
        return faces;
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
