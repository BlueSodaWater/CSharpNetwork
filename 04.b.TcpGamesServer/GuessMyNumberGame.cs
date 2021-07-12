using System;
using System.Net.Sockets;
using System.Threading;

namespace TcpGames
{
    public class GuessMyNumberGame : IGame
    {
        // 游戏的对象
        private TcpGamesServer _server;
        private TcpClient _player;
        private Random _rng;
        private bool _needToDisconnectClient = false;

        // 游戏名
        public string Name
        {
            get { return "Guess My Number"; }
        }

        // 只需要一个玩家
        public int RequiredPlayers
        {
            get { return 1; }
        }

        // 构造器
        public GuessMyNumberGame(TcpGamesServer server)
        {
            _server = server;
            _rng = new Random();
        }

        // 给游戏增加一个玩家
        public bool AddPlayer(TcpClient client)
        {
            // 确保玩家加入进来了
            if (_player == null)
            {
                _player = client;
                return true;
            }

            return false;
        }

        // 如果断开连接客户端是我们的，我们需要退出游戏
        public void DisconnectClient(TcpClient client)
        {
            _needToDisconnectClient = (client == _player);
        }

        // 游戏主循环
        // 尽管包是同步发送的
        public void Run()
        {
            // 确保我们有一个玩家
            bool running = (_player != null);
            if (running)
            {
                // 发送介绍信息包
                Packet introPacket = new Packet("message",
                    "Welcome player, I want you to guess my number.\n" +
                    "It's somewhere between (and including) 1 and 100.\n");
                _server.SendPacket(_player, introPacket).GetAwaiter().GetResult();
            }
            else
                return;

            // 应该在 [1, 100]
            int theNumber = _rng.Next(1, 101);
            Console.WriteLine("Our number is: {0}", theNumber);

            // 游戏一些状态变量
            bool correct = false;
            bool clientConnected = true;
            bool clientDisconnectedGracefully = false;

            // 主循环
            while (running)
            {
                // 轮询输入
                Packet inputPacket = new Packet("input", "Your guess: ");
                _server.SendPacket(_player, inputPacket).GetAwaiter().GetResult();

                // 读取回答
                Packet answerPacket = null;
                while (answerPacket == null)
                {
                    answerPacket = _server.ReceivePacket(_player).GetAwaiter().GetResult();
                    Thread.Sleep(10);
                }

                // 检查是否优雅断开连接
                if (answerPacket.Command == "bye")
                {
                    _server.HandleDisconnectedClient(_player);
                    clientDisconnectedGracefully = true;
                }

                // 检查输入
                if (answerPacket.Command == "input")
                {
                    Packet responsePacket = new Packet("message");

                    int theirGuess;
                    if (int.TryParse(answerPacket.Message, out theirGuess))
                    {
                        // 查看他们是否猜对了
                        if (theirGuess == theNumber)
                        {
                            correct = true;
                            responsePacket.Message = "Correct! You win!\n";
                        }
                        else if (theirGuess < theNumber)
                            responsePacket.Message = "Too low.\n";
                        else if (theirGuess > theNumber)
                            responsePacket.Message = "Too high.\n";
                    }
                    else
                        responsePacket.Message = "That wasn't a valid number, try again.\n";

                    // 发送消息
                    _server.SendPacket(_player, responsePacket).GetAwaiter().GetResult();
                }

                // 暂停一会
                Thread.Sleep(10);

                // 如果他们不正确，继续执行
                running &= !correct;

                // 检查是否断开连接，可能之前已经优雅的断开连接
                if (!_needToDisconnectClient && !clientDisconnectedGracefully)
                    clientConnected &= !TcpGamesServer.IsDisconnected(_player);
                else
                    clientConnected = false;

                running &= clientConnected;
            }

            // 感谢玩家然后断开连接
            if (clientConnected)
                _server.DisconnectClient(_player, "Thanks for playing \"Guess My Number\"!");
            else
                Console.WriteLine("Client disconnected from game.");

            Console.WriteLine("Ending a \"{0}\" game.", Name);
        }
    }
}