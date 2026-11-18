namespace Ros.Transport
{
    // 客户端 → 服务器：AI 专用技能释放（与键盘技能键分开，AI 接管期间键盘会被服务器丢弃）
    public struct CSAiSkillCast
    {
        public int skillId;
    }

    public struct CSAiSkillCastSerializer
    {
        public static bool Serialize(CSAiSkillCast value, byte[] result, ref int indexStart)
        {
            return IntSerializer.Serialize(value.skillId, result, ref indexStart);
        }

        public static CSAiSkillCast Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSAiSkillCast()
            {
                skillId = IntSerializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
