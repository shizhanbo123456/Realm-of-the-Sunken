using System;
using System.Globalization;
using Ros.Config;
using Ros.Transport;
using UnityEngine;

namespace Ros.Gm
{
    // GM 指令共用执行入口：GM 面板与聊天系统页签都走这里，文本结果交给调用方输出
    public static class GmCommandRunner
    {
        // 参数类型编码，与配表 paramType 一致
        public const int ParamInt = 0;
        public const int ParamBool = 1;
        public const int ParamFloat = 2;
        public const int ParamVector2 = 3;
        public const int ParamVector3 = 4;

        public struct Arg
        {
            public int i;
            public float v0, v1, v2;
        }

        public static bool Online => Tool.NetworkManager != null && NetworkManager.CanSendWorldCommand;

        // 当前相位是否允许这条指令
        public static bool Available(gm_command row)
        {
            if (!Online) return row.home ?? false;
            return NetworkManager.BattleRunning ? row.battle ?? false : row.lobby ?? false;
        }

        // 按「命令名 + 副命令」查表：副命令不唯一（server/debug 都有 fps），必须两者一起给
        public static bool TryFind(string commandName, string subcommand, out gm_command row)
        {
            foreach (var candidate in GmCommandConfig.ordered)
            {
                if (!string.Equals(candidate.commandName, commandName, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.Equals(candidate.subcommand, subcommand, StringComparison.OrdinalIgnoreCase)) continue;
                row = candidate;
                return true;
            }
            row = default;
            return false;
        }

        // 执行：本地指令直接处理，其余发服务器；结果文本通过 output 回调给出
        public static void Run(gm_command row, Arg arg, Action<string> output)
        {
            if (RunLocal(row, arg, output)) return;
            if (!Online) { output($"{row.subMenuName}：需要联网执行"); return; }
            Tool.NetworkManager.SendGm(new CSGm { type = row.key, i = arg.i, v0 = arg.v0, v1 = arg.v1, v2 = arg.v2 });
        }

        // 参数解析：int/bool/float 直接读，Vector2/3 用 "(1.1,2.2)" 形式
        public static bool TryParseParam(gm_command row, string raw, out Arg arg, out string error)
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

        // 本地指令（不联网也生效；返回 true 表示已处理）
        private static bool RunLocal(gm_command row, Arg arg, Action<string> output)
        {
            switch (row.key)
            {
                case GmCommandId.SettlementView:
                    if (Tool.UIManager != null) Tool.UIManager.Settlement.PlayDemo();
                    output("已播放结算界面演示");
                    return true;

                case GmCommandId.Delay:
                    if (arg.i < 0) { output("延迟不能为负"); return true; }
                    if (Tool.Instance != null) Tool.Instance.delay = arg.i;
                    output($"本地延迟已设为 {arg.i} ms");
                    return true;

                case GmCommandId.DebugFps:
                    output($"{(1f / Mathf.Max(Time.unscaledDeltaTime, 1e-5f)).ToString("0")} FPS");
                    return true;

                case GmCommandId.DebugEntityCount:
                    output($"客户端实体数量：{CountEntities()}");
                    return true;

                case GmCommandId.ClientTime:
                    output($"客户端时间：{Time.time.ToString("0.0")}");
                    return true;

                case GmCommandId.MeshSurfaceCount:
                    output($"模型网格面数：{CountSceneMeshFaces()}");
                    return true;

                case GmCommandId.TempCredential:
                    if (Tool.SaveManager == null) { output("临时凭证：当前场景没有 SaveManager"); return true; }
                    Tool.SaveManager.UseTemporaryCredential();
                    if (Tool.UIManager != null) Tool.UIManager.Home?.RefreshCredential();
                    output($"已换临时凭证：令牌 {Tool.SaveManager.token}，名字 {ClientSelection.playerName}（重新连接生效，退出不落盘）");
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
    }
}
