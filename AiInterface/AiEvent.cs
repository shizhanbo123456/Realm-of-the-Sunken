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
    public const int Observation = 20400;   // 玩家已经看到的文本，载荷 AiObservation

    public static int Command(int commandId) => CommandBase + commandId;

    public static int Intent(AiIntent kind) => IntentBase + (int)kind;
}

// 命令事件总线：同一阶段只能有一个归属方，重复注册当场抛出来
public static class AiCommandBus
{
    public static void Bind(int commandId, Action<AiRequest> handler)
    {
        int id = AiEvent.Command(commandId);
        if (EventManager.HasEventSubscriber(id))
            throw new InvalidOperationException($"[AI] 命令 {commandId} 已有归属方，不能再注册");
        EventManager.AddEvent(id, handler);
    }

    public static void Unbind(int commandId, Action<AiRequest> handler)
    {
        EventManager.RemoveEvent(AiEvent.Command(commandId), handler);
    }

    public static bool Ready(int commandId) => EventManager.HasEventSubscriber(AiEvent.Command(commandId));

    public static void Call(int commandId, AiRequest request) => EventManager.TrigEvent(AiEvent.Command(commandId), request);

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

// 一条命令的请求：参数已按配表解析好，处理方在同一帧里回一句文本
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

// 一条玩家已经看到的文本：客户端时间 + 分类 + 内容，聊天另外带频道
public sealed class AiObservation
{
    public const string ChatCategory = "聊天";
    public const string PromptCategory = "提示";
    public const string BattleCategory = "战斗";
    public const string FlowCategory = "流程";
    public const int NoChannel = -1;

    public readonly float At;
    public readonly string Category;
    public readonly int Channel;
    public readonly string Text;
    public readonly bool FromSelf;

    private AiObservation(float at, string category, int channel, string text, bool fromSelf)
    {
        At = at;
        Category = category;
        Channel = channel;
        Text = text;
        FromSelf = fromSelf;
    }

    public static void Report(string category, string text)
    {
        EventManager.TrigEvent(AiEvent.Observation, new AiObservation(Time.time, category, NoChannel, text, false));
    }

    public static void ReportChat(int channel, string text, bool fromSelf)
    {
        EventManager.TrigEvent(AiEvent.Observation, new AiObservation(Time.time, ChatCategory, channel, text, fromSelf));
    }
}
