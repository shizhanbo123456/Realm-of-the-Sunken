// AI 的观察缓冲：只存玩家已经看到过的内容，带客户端时间；读的时候按分类筛，满了丢最老的
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public sealed class AiObservationLog
{
    private const int Capacity = 400;

    private readonly List<AiObservation> entries = new();
    private readonly StringBuilder builder = new();

    public void Append(AiObservation observation)
    {
        entries.Add(observation);
        while (entries.Count > Capacity) entries.RemoveAt(0);
    }

    public void Clear() => entries.Clear();

    // 最近 count 条，不分类别
    public string Read(int count) => Build(-1, count);

    // 只读聊天：channel 0 全部 / 1 阵营 / 2 附近 / 3 系统
    public string ReadChat(int channel, int count) => Build(channel, count);

    private string Build(int channel, int count)
    {
        // 从后往前数够 count 条，再按时间正序拼
        int start = -1;
        int picked = 0;
        for (int i = entries.Count - 1; i >= 0 && picked < count; i--)
        {
            if (!Matches(entries[i], channel)) continue;
            start = i;
            picked++;
        }
        if (start < 0) return null;

        builder.Clear();
        for (int i = start; i < entries.Count; i++)
        {
            if (!Matches(entries[i], channel)) continue;
            if (builder.Length > 0) builder.Append('\n');
            var entry = entries[i];
            builder.Append('[').Append(entry.At.ToString("0.00", CultureInfo.InvariantCulture)).Append(']').Append(entry.Text);
        }
        return builder.ToString();
    }

    private static bool Matches(AiObservation entry, int channel)
    {
        if (channel < 0) return true;
        if (entry.Category != AiObservation.ChatCategory) return false;
        return channel == 0 || entry.Channel == channel;
    }
}
