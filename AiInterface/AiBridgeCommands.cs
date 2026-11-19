// runtime-bridge 适配：把 AI 命令行暴露成 ai.* 命令，供外部经 TCP 调用（命令在主线程同步执行）
using UnityRuntimeBridge;

[UnityEngine.Scripting.Preserve]
public static class AiBridgeCommands
{
    private static AiCommandLine CommandLine => Tool.ClientLogicManager != null ? Tool.ClientLogicManager.Ai : null;

    // 只负责把命令塞进去：结果与中断一律从 ai.interrupts 取，调用方等中断
    [RuntimeCommand("ai.exec", "执行一条 AI 命令行命令；参数 line。结果经 ai.interrupts 取走")]
    public static object Exec(RuntimeBridgeContext ctx, RuntimeBridgeArgs args)
    {
        var ai = CommandLine;
        if (ai == null) return "错误：AI 命令行未就绪（当前进程没有客户端场景）";

        string line = args.GetString("line", "");
        if (line.Trim().Length == 0) return "错误：缺少参数 line";

        ai.Submit(line);
        return "";
    }

    [RuntimeCommand("ai.interrupts", "取走中断信息缓冲，没有则返回空字符串")]
    public static object Interrupts(RuntimeBridgeContext ctx, RuntimeBridgeArgs args)
    {
        var ai = CommandLine;
        if (ai == null) return "错误：AI 命令行未就绪（当前进程没有客户端场景）";
        return ai.Interrupts.TryDrain(out string text) ? text : "";
    }
}
