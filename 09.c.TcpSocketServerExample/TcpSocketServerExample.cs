using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Server
{
    class TcpSocketServerExample
    {
        public static int PortNumber = 6000;
        public static bool Running = false;
        public static Socket ServerSocket;


        // 按压Ctrl-C中断响应
        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            Console.WriteLine("Received SIGINT, shutting down server.");

            // 清理
            Running = false;
            ServerSocket.Shutdown(SocketShutdown.Both);
            ServerSocket.Close();
        }

        public static void Main(string[] args)
        {
            Socket clientSocket;
            byte[] msg = Encoding.ASCII.GetBytes("Hello, Client!\n");

            // 设置端点选项
            IPEndPoint serv = new IPEndPoint(IPAddress.Any, PortNumber);

            ServerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            ServerSocket.Bind(serv);

            // 开始监听连接（最大队列为5）
            ServerSocket.Listen(5);

            // 设置Ctrl-C
            Console.CancelKeyPress += InterruptHandler;
            Running = true;
            Console.WriteLine("Running the Tcp server.");

            // 主循环
            while (Running)
            {
                // 等到新客户端（块）
                clientSocket = ServerSocket.Accept();

                // 打印一些远程客户端的信息
                Console.WriteLine("Incoming connection from {0}, replying.", clientSocket.RemoteEndPoint);

                // 发送回复（块）
                clientSocket.Send(msg, SocketFlags.None);

                // 关闭连接
                clientSocket.Close();
            }
        }
    }
}
