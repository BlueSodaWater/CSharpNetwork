using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PongGame
{
    public class PongServer
    {
        // 网络相关
        private UdpClient _udpClient;
        public readonly int Port;

        // 消息
        Thread _networkThread;
        private ConcurrentQueue<NetworkMessage> _incomingMessages = new ConcurrentQueue<NetworkMessage>();
        private ConcurrentQueue<Tuple<Packet, IPEndPoint>> _outgoingMessages = new ConcurrentQueue<Tuple<Packet, IPEndPoint>>();
        private ConcurrentQueue<IPEndPoint> _sendByePacketTo = new ConcurrentQueue<IPEndPoint>();

        // 竞技场管理
        private ConcurrentDictionary<Arena, byte> _activeArenas = new ConcurrentDictionary<Arena, byte>(); // 用作为HashSet
        private ConcurrentDictionary<IPEndPoint, Arena> _playerToArenaMap = new ConcurrentDictionary<IPEndPoint, Arena>();
        private Arena _nextArena;

        // 用来检查我们是否运行客户端
        private ThreadSafe<bool> _running = new ThreadSafe<bool>(false);

        public PongServer(int port)
        {
            Port = port;

            // 创建UDP套接字（IPv6）
            _udpClient = new UdpClient(Port, AddressFamily.InterNetworkV6);
        }

        // 提示我们可以开始运行服务器
        public void Start()
        {
            _running.Value = true;
        }

        // 开始关闭服务器
        public void Shutdown()
        {
            if (_running.Value)
            {
                Console.WriteLine("[Server] Shutdown requested by user.");

                // 关闭运行的游戏
                Queue<Arena> arenas = new Queue<Arena>(_activeArenas.Keys);
                foreach (Arena arena in arenas)
                    arena.Stop();

                // 停止网络线程
                _running.Value = false;
            }
        }

        // 清除所有需要的资源
        public void Close()
        {
            _networkThread?.Join(TimeSpan.FromSeconds(10));
            _udpClient.Close();
        }

        // 简单的lambda表达式来添加竞技场
        private void AddNewArena()
        {
            _nextArena = new Arena(this);
            _nextArena.Start();
            _activeArenas.TryAdd(_nextArena, 0);
        }

        // 竞技场用来提醒服务器它结束了
        public void NotifyDone(Arena arena)
        {
            // 首先从Player->Arena中移除
            Arena a;
            if (arena.LeftPlayer.IsSet)
                _playerToArenaMap.TryRemove(arena.LeftPlayer.EndPoint, out a);
            if (arena.RightPlayer.IsSet)
                _playerToArenaMap.TryRemove(arena.RightPlayer.EndPoint, out a);

            // 从运行游戏hashset中移除
            byte b;
            _activeArenas.TryRemove(arena, out b);
        }

        // 服务器主循环
        public void Run()
        {
            // 确保我们调用了Start()
            if (_running.Value)
            {
                // Info
                Console.WriteLine("[Server] Running Ping Game");

                // 开启接受包的线程
                _networkThread = new Thread(new ThreadStart(NetworkRun));
                _networkThread.Start();

                // 开始第一个竞技场
                AddNewArena();
            }

            // 游戏服务器主循环
            bool running = _running.Value;
            while (running)
            {
                // 如果队列中有消息，将他们推出
                NetworkMessage nm;
                bool have = _incomingMessages.TryDequeue(out nm);
                if (have)
                {
                    // 根据他是什么类型的包处理它
                    if (nm.Packet.Type == PacketType.RequestJoin)
                    {
                        // 我们有一个新的客户端，将其放入竞技场
                        bool added = _nextArena.TryAddPlayer(nm.Sender);
                        if (added)
                            _playerToArenaMap.TryAdd(nm.Sender, _nextArena);

                        // 如果他们无法加入代表人满了，我们建立新的竞技场
                        if (!added)
                        {
                            AddNewArena();

                            // 现在有空间了
                            _nextArena.TryAddPlayer(nm.Sender);
                            _playerToArenaMap.TryAdd(nm.Sender, _nextArena);
                        }

                        // 分配消息
                        _nextArena.EnqueMessage(nm);
                    }
                    else
                    {
                        // 将其分配至一个已经存在的竞技场
                        Arena arena;
                        if (_playerToArenaMap.TryGetValue(nm.Sender, out arena))
                            arena.EnqueMessage(nm);
                    }
                }
                else
                    Thread.Sleep(1); // 如果没有消息休息一下

                // 检测退出
                running &= _running.Value;
            }
        }

        // 这个函数会在它自己的线程中运行
        // 从UdpClient中读/写数据
        private void NetworkRun()
        {
            if (!_running.Value)
                return;

            Console.WriteLine("[Server] Waiting for UDP datagrams on port {0}", Port);

            while (_running.Value)
            {
                bool canRead = _udpClient.Available > 0;
                int numToWrite = _outgoingMessages.Count;
                int numToDisconnect = _sendByePacketTo.Count;

                // 如果有数据就取出来
                if (canRead)
                {
                    // 读入一个数据报
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref ep); // 阻塞

                    // 将新消息入队
                    NetworkMessage nm = new NetworkMessage();
                    nm.Sender = ep;
                    nm.Packet = new Packet(data);
                    nm.ReceiveTime = DateTime.Now;

                    _incomingMessages.Enqueue(nm);
                }

                // 写出队列
                for (int i = 0; i < numToWrite; i++)
                {
                    // 发送一些数据
                    Tuple<Packet, IPEndPoint> msg;
                    bool have = _outgoingMessages.TryDequeue(out msg);
                    if (have)
                        msg.Item1.Send(_udpClient, msg.Item2);
                }

                // 提醒客户端Bye消息
                for (int i = 0; i < numToDisconnect; i++)
                {
                    IPEndPoint to;
                    bool have = _sendByePacketTo.TryDequeue(out to);
                    if (have)
                    {
                        ByePacket bp = new ByePacket();
                        bp.Send(_udpClient, to);
                    }
                }

                // 如果无事发生，休息一下
                if (!canRead && (numToWrite == 0) && (numToDisconnect == 0))
                    Thread.Sleep(1);
            }

            Console.WriteLine("[Server] Done listening for UDP datagrams");

            // 等待所有的竞技场线程加入
            Queue<Arena> arenas = new Queue<Arena>(_activeArenas.Keys);
            if (arenas.Count > 0)
            {
                Console.WriteLine("[Server] Waiting for active Areans to finish...");
                foreach (Arena arena in arenas)
                    arena.JoinThread();
            }

            // 看哪些客户端留下，通知Bye消息
            if (_sendByePacketTo.Count > 0)
            {
                Console.WriteLine("[Server] Notifying remaining clients of shutdown...");

                // 循环执行直到我们告诉其他人
                IPEndPoint to;
                bool have = _sendByePacketTo.TryDequeue(out to);
                while (have)
                {
                    ByePacket bp = new ByePacket();
                    bp.Send(_udpClient, to);
                    have = _sendByePacketTo.TryDequeue(out to);
                }
            }
        }

        // 将数据包排队发送给另一个人
        public void SendPacket(Packet packet, IPEndPoint to)
        {
            _outgoingMessages.Enqueue(new Tuple<Packet, IPEndPoint>(packet, to));
        }

        // 排队发送ByePacket到一个指定的端点
        public void SendBye(IPEndPoint to)
        {
            _sendByePacketTo.Enqueue(to);
        }

        public static PongServer server;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            // 优雅退出
            args.Cancel = true;
            server?.Shutdown();
        }

        public static void Main(string[] args)
        {
            // 设置服务器
            int port = 6000;//int.Parse(args[0].Trim());
            server = new PongServer(port);

            // 增加Ctrl-C句柄
            Console.CancelKeyPress += InterruptHandler;

            // 运行
            server.Start();
            server.Run();
            server.Close();
        }
    }
}
