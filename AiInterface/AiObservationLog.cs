// AI 的观察缓冲：聊天 / 飘字提示 / 重要事件三条各存一份，时间用战斗剩余时间，满了丢最老的
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class AiObservationLog
{
    private const int ChatCapacity = 400;
    private const int PromptCapacity = 200;
    private const int ImportantCapacity = 100;

    private readonly List<AiChatLine> chat = new();
    private readonly List<AiTextLine> prompts = new();
    private readonly List<AiTextLine> important = new();
    private readonly StringBuilder builder = new();

    // Lua 的 Tick 状态机靠总数判断有没有新内容
    public int ChatTotal { get; private set; }

    public int PromptTotal { get; private set; }

    public void AppendChat(AiChatLine line)
    {
        chat.Add(line);
        ChatTotal++;
        while (chat.Count > ChatCapacity) chat.RemoveAt(0);
    }

    public void AppendPrompt(AiTextLine line)
    {
        prompts.Add(line);
        PromptTotal++;
        while (prompts.Count > PromptCapacity) prompts.RemoveAt(0);
    }

    public void AppendImportant(AiTextLine line)
    {
        important.Add(line);
        while (important.Count > ImportantCapacity) important.RemoveAt(0);
    }

    public void Clear()
    {
        chat.Clear();
        prompts.Clear();
        important.Clear();
        ChatTotal = 0;
        PromptTotal = 0;
    }

    // channel 0 全部 / 1 阵营 / 2 附近 / 3 系统
    public string ReadChat(int channel, int count) => BuildChat(channel, count);

    public string ReadPrompt(int count) => BuildText(prompts, count);

    public string ReadImportant(int count) => BuildText(important, count);

    private string BuildChat(int channel, int count)
    {
        int start = Start(chat.Count, count, i => channel == 0 || chat[i].Channel == channel);
        if (start < 0) return null;

        builder.Clear();
        for (int i = start; i < chat.Count; i++)
        {
            if (channel != 0 && chat[i].Channel != channel) continue;
            Append(builder, chat[i].At, chat[i].Text);
        }
        return builder.ToString();
    }

    private string BuildText(List<AiTextLine> list, int count)
    {
        int start = Start(list.Count, count, null);
        if (start < 0) return null;

        builder.Clear();
        for (int i = start; i < list.Count; i++) Append(builder, list[i].At, list[i].Text);
        return builder.ToString();
    }

    // 从后往前数够 count 条，返回这段的起始下标；一条都没有返回 -1
    private static int Start(int total, int count, System.Func<int, bool> match)
    {
        int start = -1;
        int picked = 0;
        for (int i = total - 1; i >= 0 && picked < count; i--)
        {
            if (match != null && !match(i)) continue;
            start = i;
            picked++;
        }
        return start;
    }

    private static void Append(StringBuilder builder, float at, string text)
    {
        if (builder.Length > 0) builder.Append('\n');
        builder.Append('[').Append(at.ToString("0.00", CultureInfo.InvariantCulture)).Append(']').Append(text);
    }
}
