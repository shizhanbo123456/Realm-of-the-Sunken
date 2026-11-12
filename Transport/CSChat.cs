namespace Ros.Transport
{
    // 频道值即协议值，也即聊天面板页签顺序（系统消息是本地页签，不上协议）
    public enum ChatChannel
    {
        All = 0,    // 全频道：房间内所有人
        Camp = 1,   // 阵营频道：同阵营
        Nearby = 2, // 附近：发送者实体 chat_nearby_radius 米内的玩家
    }

    public class CSChat
    {
        public int channel;      // ChatChannel
        public string content = "";
    }

    public struct CSChatSerializer
    {
        public static bool Serialize(CSChat value, byte[] result, ref int indexStart)
        {
            if (!BoolSerializer.Serialize(value != null, result, ref indexStart)) return false;
            if (value == null) return true;
            if (!IntSerializer.Serialize(value.channel, result, ref indexStart)) return false;
            return StringSerializer.Serialize(value.content, result, ref indexStart);
        }

        public static CSChat Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            if (!BoolSerializer.Deserialize(data, ref indexStart, invalidIndex)) return null;
            return new CSChat()
            {
                channel = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
                content = StringSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
