using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Ros.Config
{
    // 生成的配表代码统一经此读二进制；二进制由 Config/configtool.py 产出到 Assets/StreamingAssets/Config
    public static class ConfigBinary
    {
        private const string Folder = "Config";
        private const int Version = 1;
        private static readonly byte[] Magic = { (byte)'R', (byte)'O', (byte)'S', (byte)'C' };

        public static BinaryReader Open(string table)
        {
            string path = Path.Combine(Application.streamingAssetsPath, Folder, table + ".bytes");
            if (!File.Exists(path))
                throw new FileNotFoundException($"[配表] 找不到 {path}，请先运行 Config/发布配表.bat");

            BinaryReader reader = new BinaryReader(new MemoryStream(File.ReadAllBytes(path), false), Encoding.UTF8);
            for (int i = 0; i < Magic.Length; i++)
            {
                if (reader.ReadByte() == Magic[i]) continue;
                reader.Dispose();
                throw new FormatException($"[配表] {table} 不是配表二进制（魔数不符），可能是旧文件");
            }

            int version = reader.ReadInt32();
            if (version != Version)
            {
                reader.Dispose();
                throw new FormatException($"[配表] {table} 的二进制版本 {version} 与读取端 {Version} 不一致，请重新生成");
            }

            string name = ReadString(reader);
            if (name != table)
            {
                reader.Dispose();
                throw new FormatException($"[配表] 文件名 {table} 与表内名 {name} 不一致");
            }
            return reader;
        }

        public static string ReadString(BinaryReader reader)
        {
            int length = reader.ReadInt32();
            if (length < 0)
                throw new FormatException("[配表] 字符串长度为负，二进制已损坏");
            return Encoding.UTF8.GetString(reader.ReadBytes(length));
        }

        public static TEnum ReadEnum<TEnum>(BinaryReader reader) where TEnum : struct
        {
            string name = ReadString(reader);
            if (!Enum.TryParse(name, false, out TEnum value))
                throw new FormatException($"[配表] 枚举 {typeof(TEnum).Name} 没有成员「{name}」");
            return value;
        }

        public static TRow Require<TKey, TRow>(Dictionary<TKey, TRow> data, TKey key, string table)
        {
            if (data == null)
                throw new InvalidOperationException($"[配表] {table} 未加载：ConfigManager 初始化时没有执行到这张表");
            if (data.TryGetValue(key, out TRow row)) return row;
            throw new KeyNotFoundException($"[配表] {table} 缺少主键「{key}」");
        }
    }
}
