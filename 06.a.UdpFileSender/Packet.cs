using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UdpFileTransfer
{
    public class Packet
    {
        public static UInt32 Ack = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("ACK "), 0);
        public static UInt32 Bye = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("Bye "), 0);
        public static UInt32 RequestFile = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("REQF "), 0);
        public static UInt32 RequestBlock = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("REQB "), 0);
        public static UInt32 Info = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("INFO "), 0);
        public static UInt32 Send = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("SEND "), 0);

        // 数据包的字段
        public UInt32 PacketType { get; set; }
        public byte[] Payload { get; set; } = new byte[0];

        public bool IsAck { get { return PacketType == Ack; } }
        public bool IsBye { get { return PacketType == Bye; } }
        public bool IsRequestFile { get { return PacketType == RequestFile; } }
        public bool IsRequestBlock { get { return PacketType == RequestBlock; } }
        public bool IsInfo { get { return PacketType == Info; } }
        public bool IsSend { get { return PacketType == Send; } }
        public bool IsUnknown { get { return !(IsAck || IsBye || IsRequestFile || IsRequestBlock || IsInfo || IsSend); } }

        public string MessageTypeString { get { return Encoding.UTF8.GetString(BitConverter.GetBytes(PacketType)); } }

        public Packet(UInt32 packetType)
        {
            // 设置消息类型
            PacketType = packetType;
        }

        // 从自己数组中创建包
        public Packet(byte[] bytes)
        {
            PacketType = BitConverter.ToUInt32(bytes, 0); // 将会抓取前四个字节（他们的类型）

            // Payload 从第四个字节开始
            Payload = new byte[bytes.Length - 4];
            bytes.Skip(4).ToArray().CopyTo(Payload, 0);
        }

        public override string ToString()
        {
            // 获取前几个字节并且转化为字符串
            String payloadStr;
            int payloadSize = Payload.Length;
            if (payloadSize > 8)
                payloadStr = Encoding.ASCII.GetString(Payload, 0, 8) + "...";
            else
                payloadStr = Encoding.ASCII.GetString(Payload, 0, payloadSize);

            // 类别字符串
            String typeStr = "UNKNOWN";
            if (!IsUnknown)
                typeStr = MessageTypeString;

            return string.Format(
                "[Packet:\n" +
                "  Type={0},\n" +
                "  PayloadSize={1},\n" +
                "  Payload=`{2}`]",
                typeStr, payloadSize, payloadStr);
        }

        // 以字节数组的形式获取包
        public byte[] GetBytes()
        {
            // 合并字节数组
            byte[] bytes = new byte[4 + Payload.Length];
            BitConverter.GetBytes(PacketType).CopyTo(bytes, 0);
            Payload.CopyTo(bytes, 4);

            return bytes;
        }
    }

    // ACK
    public class AckPacket : Packet
    {
        public string Message
        {
            get { return Encoding.UTF8.GetString(Payload); }
            set { Payload = Encoding.UTF8.GetBytes(value); }
        }

        public AckPacket(Packet p = null) : base(Ack)
        {
            if (p != null)
                Payload = p.Payload;
        }
    }

    // REQF
    public class RequestFilePacket : Packet
    {
        public string Filename
        {
            get { return Encoding.UTF8.GetString(Payload); }
            set { Payload = Encoding.UTF8.GetBytes(value); }
        }

        public RequestFilePacket(Packet p = null) : base(RequestFile)
        {
            if (p != null)
                Payload = p.Payload;
        }
    }

    // REQB
    public class RequestBlockPacket : Packet
    {
        public UInt32 Number
        {
            get { return BitConverter.ToUInt32(Payload, 0); }
            set { Payload = BitConverter.GetBytes(value); }
        }

        public RequestBlockPacket(Packet p = null) : base(RequestBlock)
        {
            if (p != null)
                Payload = p.Payload;
        }
    }

    // INFO
    public class InfoPacket : Packet
    {
        // 应该是MD5校验和
        public byte[] Checksum
        {
            get { return Payload.Take(16).ToArray(); }
            set { value.CopyTo(Payload, 0); }
        }

        public UInt32 FileSize
        {
            get { return BitConverter.ToUInt32(Payload.Skip(16).Take(4).ToArray(), 0); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, 16); }
        }

        public UInt32 MaxBlockSize
        {
            get { return BitConverter.ToUInt32(Payload.Skip(16 + 4).Take(4).ToArray(), 0); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, 16 + 4); }
        }

        public UInt32 BlockCount
        {
            get { return BitConverter.ToUInt32(Payload.Skip(16 + 4 + 4).Take(4).ToArray(), 0); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, 16 + 4 + 4); }
        }

        public InfoPacket(Packet p = null) : base(Info)
        {
            if (p != null)
                Payload = p.Payload;
            else
                Payload = new byte[16 + 4 + 4 + 4];
        }
    }

    // SEND
    public class SendPacket : Packet
    {
        public Block Block
        {
            get { return new Block(Payload); }
            set { Payload = value.GetBytes(); }
        }

        public SendPacket(Packet p = null) : base(Send)
        {
            if (p != null)
                Payload = p.Payload;
        }
    }
}
