using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Client
{
    class TcpSocketClientExample
    {
        public static int MaxReceiveLength = 255;
        public static int PortNumber = 6000;

        public static void Main(string[] args)
        {
            int len;
            byte[] buffer = new byte[MaxReceiveLength + 1];

            // 创建TCP/IP套接字
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint serv = new IPEndPoint(IPAddress.Loopback, PortNumber);

            // 连接服务器
            Console.WriteLine("Connecting to server...");
            clientSocket.Connect(serv);

            // 获取消息（块）
            len = clientSocket.Receive(buffer);
            Console.Write("Got a message from the server[{0} bytes]:\n{1}",
                len, Encoding.ASCII.GetString(buffer, 0, len));

            clientSocket.Close();
        }
    }
}
