using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace UdpFileTransfer
{
    // 这有一个简单的数据结构，用在包队列中
    public class NetworkMessage
    {
        public IPEndPoint Sender { get; set; }
        public Packet Packet { get; set; }
    }
}
