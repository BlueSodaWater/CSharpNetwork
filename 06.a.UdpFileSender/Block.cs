using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpFileTransfer
{
    // 这些是将通过网络发送的数据块
    public class Block
    {
        public UInt32 Number { get; set; }
        public byte[] Data { get; set; } = new byte[0];

        // 使用提供的数字创建一个新的数据块
        public Block(UInt32 number = 0)
        {
            Number = Number;
        }

        // 从字节数组创建一个块
        public Block(byte[] bytes)
        {
            // 前四个字节是数字
            Number = BitConverter.ToUInt32(bytes, 0);

            // 从第四个字节开始是数据
            Data = bytes.Skip(4).ToArray();
        }

        public override string ToString()
        {
            // 取前几位数据并将其转换为字符串
            String dataStr;
            if (Data.Length > 8)
                dataStr = Encoding.ASCII.GetString(Data, 0, 8) + "...";
            else
                dataStr = Encoding.ASCII.GetString(Data, 0, Data.Length);

            return string.Format(
                "[Block:\n" +
                "  Number={0},\n" +
                "  Size={1},\n" +
                "  Data=`{2}`]",
                Number, Data.Length, dataStr);
        }

        // 将块中的数据以字节数组的形式返回
        public byte[] GetBytes()
        {
            // 转换元数据
            byte[] numberBytes = BitConverter.GetBytes(Number);

            // 将数据联合到更大的数组
            byte[] bytes = new byte[numberBytes.Length + Data.Length];
            numberBytes.CopyTo(bytes, 0);
            Data.CopyTo(bytes, 4);

            return bytes;
        }
    }
}
