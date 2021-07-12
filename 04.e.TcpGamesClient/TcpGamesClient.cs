using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TcpGames
{
    public class TcpGamesClient
    {
        // 连接对象
        public readonly string ServerAddress;
        public readonly int Port;
        public bool Running { get; private set; }
        private TcpClient _client;
        private bool _clientRequestDisconnect = false;

        // 消息
        private NetworkStream _msgStream = null;
        private Dictionary<string, Func<string, Task>> _commandHandlers = new Dictionary<string, Func<string, Task>>();

        public TcpGamesClient(string serverAddress, int port)
        {
            // 创建一个无连接的TcpClient
            _client = new TcpClient();
            Running = false;

            // 设置其他数据
            ServerAddress = serverAddress;
            Port = port;
        }

        // 清除剩下的网络资源
        private void CleanNetworkResources()
        {
            _msgStream?.Close();
            _msgStream = null;
            _client.Close();
        }

        // 连接到游戏服务器
        public void Connect()
        {
            // 连接到服务器
            try
            {
                _client.Connect(ServerAddress, Port); // 将会为我们解析DNS
            }
            catch (SocketException se)
            {
                Console.WriteLine("[ERROR] {0}", se.Message);
            }

            // 检测我们是否连接上
            if (_client.Connected)
            {
                // 连接成功
                Console.WriteLine("Connected to the server at {0}.", _client.Client.RemoteEndPoint);
                Running = true;

                // 获取信息流
                _msgStream = _client.GetStream();

                // 挂起一些数据包处理命令
                _commandHandlers["bye"] = HandleBye;
                _commandHandlers["message"] = HandleMessage;
                _commandHandlers["input"] = HandleInput;
            }
            else
            {
                CleanNetworkResources();
                Console.WriteLine("Wasn't able to connect to the server at {0}:{1}.", ServerAddress, Port);
            }
        }

        // 需要断开链接，将会给服务器发送一个“bye”消息
        // 只能由客户端调用
        public void Disconnect()
        {
            Console.WriteLine("Disconnecting from the server...");
            Running = false;
            _clientRequestDisconnect = true;
            SendPacket(new Packet("bye")).GetAwaiter().GetResult();
        }

        // 游戏客户端主循环
        public void Run()
        {
            bool wasRunning = Running;

            // 监听消息
            List<Task> tasks = new List<Task>();
            while (Running)
            {
                // 检测新的包
                tasks.Add(HandleIncomingPackets());

                // 减少CPU的使用频率
                Thread.Sleep(10);

                // 确保我们没有不优雅的断开连接
                if (IsDisconnected(_client) && !_clientRequestDisconnect)
                {
                    Running = false;
                    Console.WriteLine("The server has disconnected from us ungracefully.\n:[");
                }
            }

            // 防止我们有多个包，给他们1秒钟来处理
            Task.WaitAll(tasks.ToArray(), 1000);

            // 清除
            CleanNetworkResources();
            if (wasRunning)
                Console.WriteLine("Disconnected.");
        }

        // 异步给服务器发送包
        private async Task SendPacket(Packet packet)
        {
            try
            {
                // 将JSON转化为buffer，并且将其长度转化为16位无符号整数buffer
                byte[] jsonBuffer = Encoding.UTF8.GetBytes(packet.ToJson());
                byte[] lengthBuffer = BitConverter.GetBytes(Convert.ToUInt16(jsonBuffer.Length));

                // 连接buffer
                byte[] packerBuffer = new byte[lengthBuffer.Length + jsonBuffer.Length];
                lengthBuffer.CopyTo(packerBuffer, 0);
                jsonBuffer.CopyTo(packerBuffer, lengthBuffer.Length);

                // 发送包
                await _msgStream.WriteAsync(packerBuffer, 0, packerBuffer.Length);
            }
            catch (Exception) { }
        }

        // 检查即将到来的消息并且处理他们
        // 这个方法一次将会处理一个包，即使memory stream中有不止一个
        private async Task HandleIncomingPackets()
        {
            try
            {
                // 检查即将到来的消息并且处理
                if (_client.Available > 0)
                {
                    // 当数据存在时候，前两个字节是包的大小
                    byte[] lengthBuffer = new byte[2];
                    await _msgStream.ReadAsync(lengthBuffer, 0, 2);
                    ushort packetByteSize = BitConverter.ToUInt16(lengthBuffer, 0);

                    // 读取流的剩余内容，里面就是包的数据
                    byte[] jsonBuffer = new byte[packetByteSize];
                    await _msgStream.ReadAsync(jsonBuffer, 0, jsonBuffer.Length);

                    // 将其转化为packet数据类型
                    string jsonString = Encoding.UTF8.GetString(jsonBuffer);
                    Packet packet = Packet.FromJson(jsonString);

                    // 调度他
                    try
                    {
                        await _commandHandlers[packet.Command](packet.Message);
                    }
                    catch (KeyNotFoundException) { }
                }
            }
            catch (Exception) { }
        }

        private Task HandleBye(string message)
        {
            // 打印消息
            Console.WriteLine("The server is disconnecting us with this message:");
            Console.WriteLine(message);

            // 将会在Run()中启用断开连接进程
            Running = false;
            return Task.FromResult(0);
        }

        // 将服务器的消息打印出来
        private Task HandleMessage(string message)
        {
            Console.Write(message);
            return Task.FromResult(0);
        }

        // 从用户获取输入然后将其发送给服务器
        private async Task HandleInput(string message)
        {
            // 打印提示并获取响应
            Console.Write(message);
            string responseMsg = Console.ReadLine();

            // 发送响应
            Packet resp = new Packet("input", responseMsg);
            await SendPacket(resp);
        }

        // 检测客户端是否优雅断开连接
        private static bool IsDisconnected(TcpClient client)
        {
            try
            {
                Socket s = client.Client;
                return s.Poll(10 * 1000, SelectMode.SelectRead) && (s.Available == 0);
            }
            catch (SocketException)
            {
                // 出现socket错误的时候，认定为断开连接
                return true;
            }
        }


        public static TcpGamesClient gamesClient;

        public static void InterruptHandler(object sender, ConsoleCancelEventArgs args)
        {
            // 优雅断开连接
            args.Cancel = true;
            gamesClient?.Disconnect();
        }

        public static void Main(string[] args)
        {
            // 设置游戏客户端
            string host = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());
            gamesClient = new TcpGamesClient(host, port);

            // 增加Ctrl-C按压事件
            Console.CancelKeyPress += InterruptHandler;

            // 试图和服务器连接和交互
            gamesClient.Connect();
            gamesClient.Run();
        }
    }
}
