// AI 命令与观察的事件契约：事件 id 定死在这里，链路归属方在各自生命周期里注册，无订阅即当前阶段不可用
using System;
using System.Collections.Generic;
using Ros.Config;
using UnityEngine;

// 意图上行方式：客户端只说「想要什么」，怎么执行由服务器行为层决定
public enum AiIntent
{
    Stand,
    MoveTo,
    AttackTarget,
    Stop,
}

public static class AiEvent
{
    public const int CommandBase = 20000;   // 20000~20099：命令行命令，载荷 AiRequest
    public const int IntentBase = 20200;    // 20200~20203：Lua 意图，载荷 AiIntentRequest
    public const int ChatLine = 20400;      // 聊天频道一行，载荷 AiChatLine
    public const int PromptLine = 20401;    // 飘字提示一行，载荷 AiTextLine
    public const int ImportantLine = 20402; // 重要事件一行，载荷 AiTextLine

    public static int Command(int commandId) => CommandBase + commandId;

    public static int Intent(AiIntent kind) => IntentBase + (int)kind;
}

// 命令事件总线：同一阶段只能有一个归属方，重复注册当场抛出来
public static class AiCommandBus
{
    public static void Bind(ai_commandEnum command, Action<AiRequest> handler)
    {
        int id = AiEvent.Command((int)command);
        if (EventManager.HasEventSubscriber(id))
            throw new InvalidOperationException($"[AI] 命令 {command} 已有归属方，不能再注册");
        EventManager.AddEvent(id, handler);
    }

    public static void Unbind(ai_commandEnum command, Action<AiRequest> handler)
    {
        EventManager.RemoveEvent(AiEvent.Command((int)command), handler);
    }

    public static bool Ready(ai_commandEnum command) => EventManager.HasEventSubscriber(AiEvent.Command((int)command));

    public static void Call(ai_commandEnum command, AiRequest request) =>
        EventManager.TrigEvent(AiEvent.Command((int)command), request);

    public static void BindIntent(AiIntent kind, Action<AiIntentRequest> handler)
    {
        int id = AiEvent.Intent(kind);
        if (EventManager.HasEventSubscriber(id))
            throw new InvalidOperationException($"[AI] 意图 {kind} 已有归属方，不能再注册");
        EventManager.AddEvent(id, handler);
    }

    public static void UnbindIntent(AiIntent kind, Action<AiIntentRequest> handler)
    {
        EventManager.RemoveEvent(AiEvent.Intent(kind), handler);
    }

    // 没注册就说明当前阶段不该发意图，直接拒给调用方；返回 false 表示当前阶段没有意图通道
    public static bool SendIntent(AiIntent kind, AiIntentRequest request)
    {
        int id = AiEvent.Intent(kind);
        if (!EventManager.HasEventSubscriber(id))
        {
            request.Reject("当前不在战斗中");
            return false;
        }
        EventManager.TrigEvent(id, request);
        return true;
    }
}

// 一条命令的请求：参数已按配表解析好，处理方回一句文本（无返回值的命令不回）
public sealed class AiRequest
{
    private readonly object[] values;

    public readonly string Command;
    public readonly Action<string> Reply;

    public AiRequest(string command, object[] values, Action<string> reply)
    {
        Command = command;
        this.values = values;
        Reply = reply;
    }

    public int Int(int index) => (int)values[index];

    public float Float(int index) => (float)values[index];

    public bool Bool(int index) => (bool)values[index];

    public string Str(int index) => (string)values[index];

    public List<int> IntList(int index) => (List<int>)values[index];

    public void Reject(string text) => Reply("错误：" + text);

    public void Reject(promptEnum prompt) => Reject(PromptConfig.Get(prompt).text);
}

// Lua 发起的意图上行：不关心返回文本，只看有没有被拒
public sealed class AiIntentRequest
{
    public readonly Vector2 Point;
    public readonly ushort TargetId;

    public bool Rejected { get; private set; }

    public string Error { get; private set; }

    public AiIntentRequest(Vector2 point, ushort targetId)
    {
        Point = point;
        TargetId = targetId;
    }

    public void Reject(string text)
    {
        Rejected = true;
        Error = text;
    }
}

// 聊天频道的一行：时间 + 频道 + 文本 + 是否自己发的
public struct AiChatLine
{
    public float At;
    public int Channel;
    public string Text;
    public bool FromSelf;
}

// 飘字提示 / 重要事件的一行：时间 + 文本
public struct AiTextLine
{
    public float At;
    public string Text;
}

// 玩家看得到的文本上报点：时间一律取战斗剩余时间（非战斗阶段为 0）
public static class AiObservation
{
    public static float Now => Tool.BattleManager != null ? Tool.BattleManager.BattleRemainTime : 0f;

    public static void ReportChat(int channel, string text, bool fromSelf)
    {
        EventManager.TrigEvent(AiEvent.ChatLine, new AiChatLine { At = Now, Channel = channel, Text = text, FromSelf = fromSelf });
    }

    public static void ReportPrompt(string text)
    {
        EventManager.TrigEvent(AiEvent.PromptLine, new AiTextLine { At = Now, Text = text });
    }

    public static void ReportImportant(string text)
    {
        EventManager.TrigEvent(AiEvent.ImportantLine, new AiTextLine { At = Now, Text = text });
    }
}
