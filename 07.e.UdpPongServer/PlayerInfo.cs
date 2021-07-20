using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace PongGame
{
    // 服务器来管理客户端的数据结构
    public class PlayerInfo
    {
        public Paddle Paddle;
        public IPEndPoint EndPoint;
        public DateTime LastPacketReceivedTime = DateTime.MinValue; // 从服务器时间
        public DateTime LastPacketSentTime = DateTime.MinValue; // 从服务器时间
        public long LastPacketReceivedTimestamp = 0; // 从客户端时间
        public bool HavePaddle = false;
        public bool Ready = false;

        public bool IsSet
        {
            get { return EndPoint != null; }
        }
    }
}
