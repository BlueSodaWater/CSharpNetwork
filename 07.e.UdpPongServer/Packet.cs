using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace PongGame
{
    // 用来定义每个包的含义
    // 他们有不同的负载（如果有的话）
    // 查看Packet下面的子类
    public enum PacketType : uint
    {
        RequestJoin = 1, // 客户端请求加入游戏
        AcceptJoin, // 服务器接受加入
        AcceptJoinAck, // 客户端知道服务器接受
        Heartbeat, // 客户端告诉服务器他还在线（游戏开始前）
        HeartbeatAck, // 服务器知道客户端的心跳（游戏开始前）
        GameStart, // 服务器告诉客户端游戏开始了
        GameStartAck, // 客户端知道游戏开始了
        PaddlePosition, // 客户端告诉板的位置
        GameState, // 服务器告诉客户端板和球的位置和得分
        PlaySoundEffect, // 服务器告诉客户端打开声音
        Bye, // 服务器和客户端告诉另一方要断开连接
    }

    public class Packet
    {
        // 包数据
        public PacketType Type;
        public long Timestamp; // 来自DateTaime.Ticks的64字节时间戳
        public byte[] Payload = new byte[0];

        // 用设定的类型和空的负载创建包
        public Packet(PacketType type)
        {
            this.Type = type;
            Timestamp = DateTime.Now.Ticks;
        }

        // 从字节数组中创建包
        public Packet(byte[] bytes)
        {
            // 开始剥离字节数组中的数据
            int i = 0;

            // 类型
            this.Type = (PacketType)BitConverter.ToUInt32(bytes, 0);
            i += sizeof(PacketType);

            // 时间戳
            Timestamp = BitConverter.ToInt64(bytes, i);
            i += sizeof(long);

            // 重设负载
            Payload = bytes.Skip(i).ToArray();
        }

        // 以字节数组的形式获取包
        public byte[] GetBytes()
        {
            int ptSize = sizeof(PacketType);
            int tsSize = sizeof(long);

            // 拼接包数据
            int i = 0;
            byte[] bytes = new byte[ptSize + tsSize + Payload.Length];

            // 类型
            BitConverter.GetBytes((uint)this.Type).CopyTo(bytes, i);
            i += ptSize;

            // 时间戳
            BitConverter.GetBytes(Timestamp).CopyTo(bytes, i);
            i += tsSize;

            // 负载
            Payload.CopyTo(bytes, i);
            i += Payload.Length;

            return bytes;
        }

        public override string ToString()
        {
            return string.Format("[Packet:{0}\n  timestamp={1}\n  payload size={2}]",
                this.Type, new DateTime(Timestamp), Payload.Length);
        }

        // 给特定的接收者发送包
        public void Send(UdpClient client, IPEndPoint receiver)
        {
            // TODO 试着异步
            byte[] bytes = GetBytes();
            client.Send(bytes, bytes.Length, receiver);
        }

        // 给默认的远程接收者发送包（如果没有设置抛出错误）
        public void Send(UdpClient client)
        {
            byte[] bytes = GetBytes();
            client.Send(bytes, bytes.Length);
        }
    }

    public class RequestJoinPacket : Packet
    {
        public RequestJoinPacket()
            : base(PacketType.RequestJoin)
        {
        }
    }

    // 服务器接受加入请求，分配一个板
    public class AcceptJoinPacket : Packet
    {
        // 板的一面
        public PaddleSide Side
        {
            get { return (PaddleSide)BitConverter.ToUInt32(Payload, 0); }
            set { Payload = BitConverter.GetBytes((uint)value); }
        }

        public AcceptJoinPacket()
            : base(PacketType.AcceptJoin)
        {
            Payload = new byte[sizeof(PaddleSide)];

            // Set a dfeault paddle of None
            Side = PaddleSide.None;
        }

        public AcceptJoinPacket(byte[] bytes)
            : base(bytes)
        {
        }
    }

    // 上面的确认包
    public class AcceptJoinAckPacket : Packet
    {
        public AcceptJoinAckPacket()
            : base(PacketType.AcceptJoinAck)
        {
        }
    }

    // 客户端告诉服务器他还在线
    public class HeartbeatPacket : Packet
    {
        public HeartbeatPacket()
            : base(PacketType.Heartbeat)
        {
        }
    }

    // 服务器告诉客户端他知道了
    public class HeartbeatAckPacket : Packet
    {
        public HeartbeatAckPacket()
            : base(PacketType.HeartbeatAck)
        {
        }
    }

    // 告诉客户端开始发送数据
    public class GameStartPacket : Packet
    {
        public GameStartPacket()
            : base(PacketType.GameStart)
        {
        }
    }

    // 知道上面的消息
    public class GameStartAckPacket : Packet
    {
        public GameStartAckPacket()
            : base(PacketType.GameStartAck)
        {
        }
    }

    // 客户端告诉服务器板的Y轴位置在哪里
    public class PaddlePositionPacket : Packet
    {
        // The Paddle's Y position
        public float Y
        {
            get { return BitConverter.ToSingle(Payload, 0); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, 0); }
        }

        public PaddlePositionPacket()
            : base(PacketType.PaddlePosition)
        {
            Payload = new byte[sizeof(float)];

            // Default value is zero
            Y = 0;
        }

        public PaddlePositionPacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format("[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  Y={3}]",
                this.Type, new DateTime(Timestamp), Payload.Length, Y);
        }
    }

    // 服务端给客户端更新游戏信息
    public class GameStatePacket : Packet
    {
        // 负载数组偏移量
        private static readonly int _leftYIndex = 0;
        private static readonly int _rightYIndex = 4;
        private static readonly int _ballPositionIndex = 8;
        private static readonly int _leftScoreIndex = 16;
        private static readonly int _rightScoreIndex = 20;

        // 左板的Y轴位置
        public float LeftY
        {
            get { return BitConverter.ToSingle(Payload, _leftYIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _leftYIndex); }
        }

        // 右板的Y轴位置
        public float RightY
        {
            get { return BitConverter.ToSingle(Payload, _rightYIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _rightYIndex); }
        }

        // 球的位置
        public Vector2 BallPosition
        {
            get
            {
                return new Vector2(
                    BitConverter.ToSingle(Payload, _ballPositionIndex),
                    BitConverter.ToSingle(Payload, _ballPositionIndex + sizeof(float))
                );
            }
            set
            {
                BitConverter.GetBytes(value.X).CopyTo(Payload, _ballPositionIndex);
                BitConverter.GetBytes(value.Y).CopyTo(Payload, _ballPositionIndex + sizeof(float));
            }
        }

        // 左板得分
        public int LeftScore
        {
            get { return BitConverter.ToInt32(Payload, _leftScoreIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _leftScoreIndex); }
        }

        // 右板得分
        public int RightScore
        {
            get { return BitConverter.ToInt32(Payload, _rightScoreIndex); }
            set { BitConverter.GetBytes(value).CopyTo(Payload, _rightScoreIndex); }
        }

        public GameStatePacket()
            : base(PacketType.GameState)
        {
            // 给负载默认数据（这里其实不应该用硬编码）
            Payload = new byte[24];

            // 设置默认数据
            LeftY = 0;
            RightY = 0;
            BallPosition = new Vector2();
            LeftScore = 0;
            RightScore = 0;
        }

        public GameStatePacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format(
                "[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  LeftY={3}" +
                "\n  RightY={4}" +
                "\n  BallPosition={5}" +
                "\n  LeftScore={6}" +
                "\n  RightScore={7}]",
                this.Type, new DateTime(Timestamp), Payload.Length, LeftY, RightY, BallPosition, LeftScore, RightScore);
        }
    }

    // 服务器告诉客户端是否打开声音
    public class PlaySoundEffectPacket : Packet
    {
        public string SFXName
        {
            get { return Encoding.UTF8.GetString(Payload); }
            set { Payload = Encoding.UTF8.GetBytes(value); }
        }

        public PlaySoundEffectPacket()
            : base(PacketType.PlaySoundEffect)
        {
            SFXName = "";
        }

        public PlaySoundEffectPacket(byte[] bytes)
            : base(bytes)
        {
        }

        public override string ToString()
        {
            return string.Format(
                "[Packet:{0}\n  timestamp={1}\n  payload size={2}" +
                "\n  SFXName={3}",
                this.Type, new DateTime(Timestamp), Payload.Length, SFXName);
        }
    }

    // 客户端或服务器发送断开连接请求
    public class ByePacket : Packet
    {
        public ByePacket()
            : base(PacketType.Bye)
        {
        }
    }
}
