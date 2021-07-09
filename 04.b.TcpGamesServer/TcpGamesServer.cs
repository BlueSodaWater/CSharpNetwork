using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpGames
{
    public class TcpGamesServer
    {
        // 监听即将到来的连接
        private TcpListener _listener;

        // 客户端对象
        private List<TcpClient> _clients = new List<TcpClient>();
        private List<TcpClient> _waitingLobby = new List<TcpClient>();

        // 游戏相关内容
        private Dictionary<TcpClient, IGame> _gameClientIsIn = new Dictionary<TcpClient, IGame>();
        private List<IGame> _games = new List<IGame>();
        private List<Thread> _gameThread = new List<Thread>();
        private IGame _nextGame;

        // 其他数据
        public readonly string Name;
        public readonly int Port;
        public bool Running { get; private set; }

        // 构造函数来创建一个新的游戏服务器
        public TcpGamesServer(string name, int port)
        {
            // 设置一些基础数据
            Name = name;
            port = port;
            Running = false;

            // 创建监听器
            _listener = new TcpListener(IPAddress.Any, Port);
        }

        // 如果服务器在运行，将其关闭
        public void Shutdown()
        {
            if (Running)
            {
                Running = false;
                Console.WriteLine("Shutting down the Game(s) Server...");
            }
        }

        // 游戏服务器主循环
        public void Run()
        {
            Console.WriteLine("Starting the \"{0}\" Game(s) Server on port {1}.", Name, Port);
            Console.WriteLine("Press Ctrl-C to shutdown the server at any time.");

            // 开始下一个游戏
            // （目前只有猜数字游戏）
            _nextGame = new GuessMyNumberGame(this);

            // 开始运行服务器
            _listener.Start();
            Running = true;
            List<Task> newConnectionTasks = new List<Task>();
            Console.WriteLine("Waiting for incoming connections...");

            while (Running)
            {
                // 处理新的客户端
                if (_listener.Pending())
                    newConnectionTasks.Add(HandleNewConnection());

                // 一旦我们有足够多的人数开启下一次游戏，将他们加入进来然后开始游戏
                if (_waitingLobby.Count >= _nextGame.RequiredPlayers)
                {
                    // 从等待厅获取玩家然后开始游戏
                    int numPlayers = 0;
                    while (numPlayers < _nextGame.RequiredPlayers)
                    {
                        // 将第一个推出
                        TcpClient player = _waitingLobby[0];
                        _waitingLobby.RemoveAt(0);

                        // 试图将其加入游戏中，如果失败，重新放回等待厅
                        if (_nextGame.AddPlayer(player))
                            numPlayers++;
                        else
                            _waitingLobby.Add(player);
                    }

                    // 在新线程中开始游戏
                    Console.WriteLine("Starting a \"{0}\" game.", _nextGame.Name);
                    Thread gameThread = new Thread(new ThreadStart(_nextGame.Run));
                    gameThread.Start();
                    _games.Add(_nextGame);
                    _gameThread.Add(gameThread);

                    // 创建一个新游戏
                    _nextGame = new GuessMyNumberGame(this);
                }

                // 检测是否有客户端等待断开连接，无论是否优雅
                // 注意：这也可以（必须）是并行的
                foreach (TcpClient client in _waitingLobby.ToArray())
                {
                    EndPoint endPoint = client.Client.RemoteEndPoint;
                    bool disconnected = false;

                    // 检测是否优雅
                    Packet p = ReceivePacket(client).GetAwaiter().GetResult();
                    disconnected = (p?.Command == "bye");

                    // 不优雅
                    disconnected |= IsDisconnected(client);

                    if (disconnected)
                    {
                        HandleDisconnectedClient(client);
                        Console.WriteLine("Client {0} has disconnected from the Game(s) Server.", endPoint);
                    }
                }

                // 休息一下
                Thread.Sleep(10);
            }

            // 我们退出循环但是有客户端连接的情况，给他们1秒来结束
            Task.WaitAll(newConnectionTasks.ToArray(), 1000);

            // 关闭所有线程，无论他们是否完成
            foreach (Thread thread in _gameThread)
                thread.Abort();

            // 断开仍然连接的客户端
            Parallel.ForEach(_clients, (client) =>
            {
                DisconnectClient(client, "The Game(s) Server is being shutdown.");
            });

            // 清除我们的资源
            _listener.Stop();

            // 信息
            Console.WriteLine("The server has been shut down.");
        }

        // 等待新连接，然后将其放入等待厅
        private async Task HandleNewConnection()
        {
            // 使用Future获取客户端
            TcpClient newClient = await _listener.AcceptTcpClientAsync();
            Console.WriteLine("New connection from {0}.", newClient.Client.RemoteEndPoint);

            // 将与他们存储并放到等待厅中
            _clients.Add(newClient);
            _waitingLobby.Add(newClient);

            // 发送欢迎消息
            string msg = String.Format("Welcome to the \"{0}\" Games Server.\n", Name);
            await SendPacket(newClient, new Packet("message", msg));
        }

        // 将会尝试和一个TcpClient优雅的来断开连接
        // 它应该适用于在游戏中或者等待厅的客户端
        public void DisconnectClient(TcpClient client, string message = "")
        {
            Console.WriteLine("Disconnecting the client from {0}.", client.Client.RemoteEndPoint);

            // 如果没有设置消息。使用默认的“Goodbye”
            if (message == "")
                message = "Goodbye.";

            // 发送"bye"消息
            Task byePacket = SendPacket(client, new Packet("bye", message));

            // 通知可能有这些客户端的游戏
            try
            {
                _gameClientIsIn[client]?.DisconnectClient(client);
            }
            catch (KeyNotFoundException) { }

            // 给客户端一点时间来发送和处理优雅断开连接请求
            Thread.Sleep(100);

            // 在最后清除所有资源
            byePacket.GetAwaiter().GetResult();
            HandleDisconnectedClient(client);
        }

        // 如果一个客户端断开连接将其资源清除
        // 不管是否优雅，会将其从客户端列表和等待厅中移除
        public void HandleDisconnectedClient(TcpClient client)
        {
            // 从列表中移除并释放资源
            _clients.Remove(client);
            _waitingLobby.Remove(client);
            CleanUpClient(client);
        }

        // 异步给客户端发送包
        public async Task SendPacket(TcpClient client, Packet packet)
        {
            try
            {
                // 将JSON转化为buffer，并将其长度转化为16位无符号buffer
                byte[] jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
                byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

                // 将buffer合并
                byte[] msgBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
                lengthBuffer.CopyTo(msgBuffer, 0);
                jsonBuffer.CopyTo(msgBuffer, lengthBuffer.Length);

                // 发送包
                await client.GetStream().WriteAsync(msgBuffer, 0, msgBuffer.Length);
            }
            catch (Exception e)
            {
                // 发送信息时出现问题
                Console.WriteLine("There was an issue receiving a packet.");
                Console.WriteLine("Reason: {0}", e.Message);
            }
        }

        // 将会从一个TcpClient获得一个包
        // 如果没有任何可用的数据或其他数据会返回null
        // 从客户端获取数据的问题
        public async Task<Packet> ReceivePacket(TcpClient client)
        {
            Packet packet = null;
            try
            {
                // 首先检测是否有可用数据
                if (client.Available == 0)
                    return null;

                NetworkStream msgStream = client.GetStream();

                // 一定有到来的数据，数据包的前两个字节的是其大小
                byte[] lengthBuffer = new byte[2];
                await msgStream.ReadAsync(lengthBuffer, 0, 2);
                ushort packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                // 现在读取流中剩余的字节，就是信息包的内容
                byte[] jsonBuffer = new byte[packetByteSize];
                await msgStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

                // 将其转化为信息包的格式
                string jsonString = Encoding.UTF8.GetString(jsonBuffer);
                packet = Packet.FromJson(jsonString);
            }
            catch (Exception e)
            {
                // 获取信息包时出现问题
                Console.WriteLine("There was an issue sending a packet to {0}.", client.Client.RemoteEndPoint);
                Console.WriteLine("Reason: {0}", e.Message);
            }
            return packet;
        }

        // 检测客户端是否优雅断开连接
        // http://stackoverflow.com/questions/722240/instantly-detect-client-disconnection-from-server-socket
        public static bool IsDisconnected(TcpClient client)
        {
            try
            {
                Socket s = client.Client;
                return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
            }
            catch (SocketException)
            {
                // We got a socket error, assume it's disconnected
                return true;
            }
        }

        // 清除一个TcpClient的资源并且关闭它
        private static void CleanUpClient(TcpClient client)
        {
            client.GetStream().Close(); // 关闭网络流
            client.Close(); // 关闭客户端
        }

        public static TcpGamesServer gamesServer;

        // 为了当用户点击Ctrl-C时，优雅断开服务器
        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            gamesServer?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // 一些参数
            string name = "Bad BBS";//args[0];
            int port = 6000;//int.Parse(args[1]);

            // 处理Ctrl-C按压事件
            Console.CancelKeyPress += InterruptHandler;

            // 创建并运行服务器
            gamesServer = new TcpGamesServer(name, port);
            gamesServer.Run();
        }
    }
}
