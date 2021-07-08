using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TcpChatViewer
{
    class TcpChatViewer
    {
        // 连接对象
        public readonly string ServerAddress;
        public readonly int Port;
        private TcpClient _client;
        public bool Running { get; private set; }
        private bool _disconnectRequested = false;

        // 缓冲区和消息
        public readonly int BufferSize = 2 * 1024; // 2KB
        private NetworkStream _msgStream = null;

        public TcpChatViewer(string serverAddress, int port)
        {
            // 创建一个无连接的TcpClient
            _client = new TcpClient(); // 其他的构造函数将会开始一个连接
            _client.SendBufferSize = BufferSize;
            _client.ReceiveBufferSize = BufferSize;
            Running = false;

            // 设置其他内容
            ServerAddress = serverAddress;
            Port = port;
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

                // 告诉他我们是浏览端
                _msgStream = _client.GetStream();
                byte[] msBuffer = Encoding.UTF8.GetBytes("viewer");
                _msgStream.Write(msBuffer, 0, msBuffer.Length); // Blocks

                // 检测我们是否仍然连接中，如果服务器没有把我们踢下线，我们就继续
                if (IsDisConnected(_client))
                {
                    Running = true;
                    Console.WriteLine("Press Ctrl-C to exit the Viewer at any time.");
                }
                else
                {
                    // 服务器不把我们看作浏览端，清除
                    CleanUpNetworkResources();
                    Console.WriteLine("The server didn't recognise us as a Viewer.\n:[");
                }
            }
            else
            {
                CleanUpNetworkResources();
                Console.WriteLine("Wasn't able to connect to the server at {0}.", endPoint);
            }
        }

        //请求断开连接
        public void Disconnect()
        {
            Running = false;
            _disconnectRequested = true;
            Console.WriteLine("Disconnecting from the chat...");
        }

        // 主循环，从服务器监听和打印消息
        public void ListenForMessages()
        {
            bool wasRunning = Running;

            // 监听消息
            while (Running)
            {
                // 是否有新消息
                int messageLength = _client.Available;
                if (messageLength > 0)
                {
                    //Console.WriteLine("New incoming message of {0} bytes", messageLength);

                    // 读取整个信息
                    byte[] msgBuffer = new byte[messageLength];
                    _msgStream.Read(msgBuffer, 0, messageLength); // Blocks

                    // An alternative way of reading
                    //int bytesRead = 0;
                    //while (bytesRead < messageLength)
                    //{
                    //    bytesRead += _msgStream.Read(_msgBuffer,
                    //                                 bytesRead,
                    //                                 _msgBuffer.Length - bytesRead);
                    //    Thread.Sleep(1);    // Use less CPU
                    //}

                    // 解码并且将其打印
                    string msg = Encoding.UTF8.GetString(msgBuffer);
                    Console.WriteLine(msg);
                }

                // 减少CPU 使用
                Thread.Sleep(10);

                // 检测是否与服务器端断开连接
                if (IsDisConnected(_client))
                {
                    Running = false;
                    Console.WriteLine("Server has disconnected from us.\n:[");
                }

                // 检测用户是否已经取消请求
                Running &= !_disconnectRequested;
            }

            // 清理
            CleanUpNetworkResources();
            if (wasRunning)
                Console.Write("Disconnected.");
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

        public static TcpChatViewer viewer;

        protected static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            viewer.Disconnect();
            args.Cancel = true;
        }

        public static void Main(string[] args)
        {
            // 设置浏览端
            string host = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            viewer = new TcpChatViewer(host, port);

            // 增加Ctrl-C按压事件
            Console.CancelKeyPress += InterruptHandler;

            // 尝试连接和发送消息
            viewer.Connect();
            viewer.ListenForMessages();
        }
    }
}
