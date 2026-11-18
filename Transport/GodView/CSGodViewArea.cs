using UnityEngine;

namespace Ros.Transport
{
    // 客户端 → 服务器：上帝视角当前显示区域（左下角 + 右上角围成的包围盒，世界 XZ）
    // 请求无状态：服务器收到就扫一遍回包，客户端按实体 id 落点位、超时自行清除
    public struct CSGodViewArea
    {
        public Vector2 min;
        public Vector2 max;
    }

    public struct CSGodViewAreaSerializer
    {
        public static bool Serialize(CSGodViewArea value, byte[] result, ref int indexStart)
        {
            if (!Vector2Serializer.Serialize(value.min, result, ref indexStart)) return false;
            return Vector2Serializer.Serialize(value.max, result, ref indexStart);
        }

        public static CSGodViewArea Deserialize(byte[] data, ref int indexStart, int invalidIndex)
        {
            return new CSGodViewArea()
            {
                min = Vector2Serializer.Deserialize(data, ref indexStart, invalidIndex),
                max = Vector2Serializer.Deserialize(data, ref indexStart, invalidIndex),
            };
        }
    }
}
