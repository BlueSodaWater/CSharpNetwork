using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace TcpChatServer
{
    class TcpChatServer
    {
        // 监听什么
        private TcpListener _listener;

        // 连接的客户端类型
        private List<TcpClient> _viewers = new List<TcpClient>();
        private List<TcpClient> _messengers = new List<TcpClient>();

        // 其他发消息端取得名字
        private Dictionary<TcpClient, string> _names = new Dictionary<TcpClient, string>();

        // 需要发送出去的消息
        private Queue<string> _messageQueue = new Queue<string>();

        // 额外数据
        public readonly string ChatName;
        public readonly int Port;
        public bool Running { get; private set; }

        // 缓冲区
        public readonly int BufferSize = 1 * 1024; // 2KB

        // 用我们提供的名字建立一个新的TCP聊天服务器
        public TcpChatServer(string chatName, int port)
        {
            // 设置基础数据
            this.ChatName = chatName;
            this.Port = port;
            this.Running = false;

            // 使得监听器监听来自任何网络设备的连接
            this._listener = new TcpListener(IPAddress.Any, Port);
        }

        // 如果服务器在运行，这个函数会关闭服务器
        public void ShutDown()
        {
            this.Running = false;
            Console.WriteLine("Shuting down server");
        }

        // 开始运行服务器，当调用 ShutDown() 的时候会被暂停
        public void Run()
        {
            // 一些信息
            Console.WriteLine("Starting the \"{0}\" TCP Chat Server on port {1}.", ChatName, Port);
            Console.WriteLine("Press Ctrl-C to shut down the server at any time.");

            // 运行服务器
            this._listener.Start();
            this.Running = true;

            // 服务器主循环
            while (Running)
            {
                // 检查新的客户端
                if (_listener.Pending())
                    HandleNewConnection();

                // 做剩下的事情
                CheckForDisconnects();
                CheckForNewMessages();
                SendMessages();

                // 减少使用CPU
                Thread.Sleep(10);
            }

            // 停止服务器，清楚所以已经连接的客户端
            foreach (TcpClient v in _viewers)
                CleanUpClient(v);
            foreach (TcpClient m in _messengers)
                CleanUpClient(m);
            _listener.Stop();

            // 一些信息
            Console.WriteLine("Server is shut down.");
        }

        private void HandleNewConnection()
        {
            // 这只是（至少）一个，看他们想要啥
            bool good = false;
            TcpClient newClient = this._listener.AcceptTcpClient(); // Blocks
            NetworkStream netStream = newClient.GetStream();

            // 修改默认缓冲区大小
            newClient.SendBufferSize = BufferSize;
            newClient.ReceiveBufferSize = BufferSize;

            // 打印一些信息
            EndPoint endPoint = newClient.Client.RemoteEndPoint;
            Console.WriteLine("Handling a new client from {0}...", endPoint);

            // 让他们自己辨识自己
            byte[] msgBuffer = new byte[BufferSize];
            int bytesRead = netStream.Read(msgBuffer, 0, msgBuffer.Length); // Blocks
            if (bytesRead > 0)
            {
                string msg = Encoding.UTF8.GetString(msgBuffer, 0, bytesRead);

                if (msg == "viewer")
                {
                    // 只需要接收信息
                    good = true;
                    this._viewers.Add(newClient);

                    Console.WriteLine("{0} is a Viewer.", endPoint);

                    // 发送一个 “欢迎信息”
                    msg = String.Format("Welcome to the \"{0}\" Chat Server!", ChatName);
                    msgBuffer = Encoding.UTF8.GetBytes(msg);
                    netStream.Write(msgBuffer, 0, msgBuffer.Length);    // Blocks
                }
                else if (msg.StartsWith("name:"))
                {
                    // 他们可能是发消息端
                    string name = msg.Substring(msg.IndexOf(':') + 1);

                    if ((name != string.Empty) && (!_names.ContainsValue(name)))
                    {
                        // 是新的客户端，把他们加进来
                        good = true;
                        this._names.Add(newClient, name);
                        this._messengers.Add(newClient);

                        Console.WriteLine("{0} is a Messenger with the name {1}.", endPoint, name);

                        // 通知浏览端有新消息来了
                        this._messageQueue.Enqueue(String.Format("{0} has joined the chat.", name));
                    }
                }
                else
                {
                    // 既不是发消息端也不是浏览端，清除掉
                    Console.WriteLine("Wasn't able to identify {0} as a Viewer or Messenger.", endPoint);
                    CleanUpClient(newClient);
                }
            }

            // 我们真的需要他们吗
            if (!good)
                newClient.Close();
        }

        // 查看是否有客户端离开聊天服务器
        private void CheckForDisconnects()
        {
            // 先检测浏览端
            foreach (TcpClient v in _viewers.ToArray())
            {
                if (IsDisConnected(v))
                {
                    Console.WriteLine("Viewer {0} has left.", v.Client.RemoteEndPoint);

                    // 将其清理掉
                    _viewers.Remove(v); // 从列表中删除
                    CleanUpClient(v);
                }
            }

            // 再检测发消息端
            foreach (TcpClient m in _messengers.ToArray())
            {
                if (IsDisConnected(m))
                {
                    // 获取发消息者的信息
                    string name = _names[m];

                    // 通知浏览端有人离开了
                    Console.WriteLine("Messeger {0} has left.", name);
                    _messageQueue.Enqueue(String.Format("{0} has left the chat", name));

                    // 将其清理掉
                    _messengers.Remove(m); // 从列表中删除
                    _names.Remove(m); // 从已取名字中清除
                    CleanUpClient(m);
                }
            }
        }

        // 查看有无发消息者给我们发送消息，将其放入队列中
        private void CheckForNewMessages()
        {
            foreach (TcpClient m in _messengers)
            {
                int messageLength = m.Available;
                if (messageLength > 0)
                {
                    // 有消息，截取他
                    byte[] msgBuffer = new byte[messageLength];
                    m.GetStream().Read(msgBuffer, 0, msgBuffer.Length); // Blocks

                    // 给其加上姓名然后将其放入队列中
                    string msg = String.Format("{0}: {1}", _names[m], Encoding.UTF8.GetString(msgBuffer));
                    _messageQueue.Enqueue(msg);
                }
            }
        }

        // 清除消息队列（然后将其发送给所有的浏览端）
        private void SendMessages()
        {
            foreach (string msg in _messageQueue)
            {
                // 将消息编码
                byte[] msgBuffer = Encoding.UTF8.GetBytes(msg);

                // 将消息发送给每个浏览端
                foreach (TcpClient v in _viewers)
                    v.GetStream().Write(msgBuffer, 0, msgBuffer.Length); // Blocks
            }

            // 清除队列
            _messageQueue.Clear();
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

        // 清除一个TcpClient的资源
        private static void CleanUpClient(TcpClient client)
        {
            client.GetStream().Close(); // 关闭网络流
            client.Close(); // 关闭客户端
        }




        public static TcpChatServer chat;

        protected static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            chat.ShutDown();
            args.Cancel = true;
        }

        public static void Main(string[] args)
        {
            // 创建服务器
            string name = "Bad IRC"; // args[0].Trim();
            int port = 6000; // int.Parse(args[1].Trim());
            chat = new TcpChatServer(name, port);

            // 增加 Ctrl-C按压事件
            Console.CancelKeyPress += InterruptHandler;

            // 运行聊天服务器
            chat.Run();
        }
    }
}
