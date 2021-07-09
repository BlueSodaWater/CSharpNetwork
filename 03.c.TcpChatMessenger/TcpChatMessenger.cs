using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TcpChatMessenger
{
    class TcpChatMessenger
    {
        // 连接对象
        public readonly string ServerAddress;
        public readonly int Port;
        private TcpClient _client;
        public bool Running { get; private set; }

        // 缓冲区和消息
        public readonly int BufferSize = 2 * 1024; // 2KB
        private NetworkStream _msgStream = null;

        //自己的数据
        public readonly string Name;

        public TcpChatMessenger(string serverAddress, int port, string name)
        {
            // 创建一个无连接的TcpClient
            _client = new TcpClient(); // 其他的构造函数将会开始一个连接
            _client.SendBufferSize = BufferSize;
            _client.ReceiveBufferSize = BufferSize;
            Running = false;

            // 设置其他内容
            ServerAddress = serverAddress;
            Port = port;
            Name = name;
        }

        public void Connect()
        {
            // 尝试连接
            _client.Connect(ServerAddress, Port); // 将会为我们解析DNS;Blocks
            EndPoint endPoint = _client.Client.RemoteEndPoint;

            // 确保我们连接上了
            if (_client.Connected)
            {
                // 连接成功
                Console.WriteLine("Connected to server at {0}.", endPoint);

                // 告诉他我们是发消息端
                _msgStream = _client.GetStream();
                byte[] msBuffer = Encoding.UTF8.GetBytes(String.Format("name:{0}", Name));
                _msgStream.Write(msBuffer, 0, msBuffer.Length); // Blocks

                // 如果我们在发送名字之后仍然连接，说明服务器接受了我们
                if (!IsDisConnected(_client))
                    Running = true;
                else
                {
                    // 名字已经被取了
                    CleanUpNetworkResources();
                    Console.WriteLine("The server rejected us; \"{0}\" is probably in use.", Name);
                }
            }
            else
            {
                CleanUpNetworkResources();
                Console.WriteLine("Wasn't able to connect to the server at {0}.", endPoint);
            }
        }

        public void SendMessages()
        {
            bool wasRunning = Running;

            while (Running)
            {
                // 等待用户输入
                Console.Write("{0}> ", Name);
                string msg = Console.ReadLine();

                // 退出或者发消息
                if ((msg.ToLower() == "quit") || (msg.ToLower() == "exit"))
                {
                    // 用户想要退出
                    Console.WriteLine("Disconnecting...");
                    Running = false;
                }
                else if (msg != string.Empty)
                {
                    // 发送消息
                    byte[] msgBuffer = Encoding.UTF8.GetBytes(msg);
                    _msgStream.Write(msgBuffer, 0, msgBuffer.Length); // Blocks
                }

                // 减少CPU的使用
                Thread.Sleep(10);

                // 检测服务器是否与我们断开连接
                if (IsDisConnected(_client))
                {
                    Running = false;
                    Console.WriteLine("Server has disconnected from us.\n:[");
                }
            }

            CleanUpNetworkResources();
            if (wasRunning)
                Console.Write("Disconnected");
        }

        // 清除剩余的网络资源
        private void CleanUpNetworkResources()
        {
            _msgStream?.Close();
            _msgStream = null;
            _client.Close();
        }

        // 检测是否有套接字断开连接
        // http://stackoverflow.com/questions/722240/instantly-detect-client-disconnection-from-server-socket
        private static bool IsDisConnected(TcpClient client)
        {
            try
            {
                Socket s = client.Client;
                return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
            }
            catch (SocketException se)
            {
                // 当我们截取异常时，就假设已经断开连接了
                return true;
            }
        }

        public static void Main(string[] args)
        {
            // Get a name
            Console.Write("Enter a name to use: ");
            string name = Console.ReadLine();

            // 设置消息
            string host = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            TcpChatMessenger messenger = new TcpChatMessenger(host, port, name);

            // 连接并发送消息
            messenger.Connect();
            messenger.SendMessages();
        }
    }
}
