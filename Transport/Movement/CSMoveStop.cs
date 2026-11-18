namespace Ros.Transport
{
    // 客户端 → 服务器：停止移动（WASD 全部松开）。与 CSMove 分开，服务器收到即清位移输入
    public struct CSMoveStop
    {
    }

    public struct CSMoveStopSerializer
    {
        // 空包：无内容，不占字节，与位传输开关无关
        public static bool Serialize(CSMoveStop value, byte[] result, ref int indexStart)
        {
            return true;
        }

        public static CSMoveStop Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSMoveStop();
        }
    }
}
