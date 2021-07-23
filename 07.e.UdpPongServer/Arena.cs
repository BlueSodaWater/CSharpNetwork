using Microsoft.Xna.Framework;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PongGame
{
    public enum ArenaState
    {
        NotRunning,
        WaitingForPlayers,
        NotifyingGameStart,
        InGame,
        GamwOver
    }

    // 这是游戏的地点
    public class Arena
    {
        // 游戏对象和状态信息
        public ThreadSafe<ArenaState> State { get; private set; } = new ThreadSafe<ArenaState>();
        private Ball _ball = new Ball();
        public PlayerInfo LeftPlayer { get; private set; } = new PlayerInfo(); // 包含板
        public PlayerInfo RightPlayer { get; private set; } = new PlayerInfo(); // 包含板
        private object _setPlayerLock = new object();
        private Stopwatch _gameTimer = new Stopwatch();

        // 连接信息
        private PongServer _server;
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(20);

        // 包队列
        private ConcurrentQueue<NetworkMessage> _messages = new ConcurrentQueue<NetworkMessage>();

        // 关闭数据
        private ThreadSafe<bool> _stopRequested = new ThreadSafe<bool>(false);

        // 其他数据
        private Thread _arenaThread;
        private Random _random = new Random();
        public readonly int Id;
        private static int _nextId = 1;

        public Arena(PongServer server)
        {
            _server = server;
            Id = _nextId++;
            State.Value = ArenaState.NotRunning;

            // 一些其他数据
            LeftPlayer.Paddle = new Paddle(PaddleSide.Left);
            RightPlayer.Paddle = new Paddle(PaddleSide.Right);
        }

        // 如果添加用户就返回true
        // 否则返回false（最多可以接受两个用户）
        public bool TryAddPlayer(IPEndPoint playerIP)
        {
            if (State.Value == ArenaState.WaitingForPlayers)
            {
                lock (_setPlayerLock)
                {
                    // 先设置左边
                    if (!LeftPlayer.IsSet)
                    {
                        LeftPlayer.EndPoint = playerIP;
                        return true;
                    }

                    // 然后设置右边
                    if (!RightPlayer.IsSet)
                    {
                        RightPlayer.EndPoint = playerIP;
                        return true;
                    }
                }
            }

            // 不能再添加了
            return false;
        }

        // 初始化游戏对象至默认状态和开启新线程
        public void Start()
        {
            // 转化状态
            State.Value = ArenaState.WaitingForPlayers;

            // 在Run方法中开启内部线程
            _arenaThread = new Thread(new ThreadStart(ArenaRun));
            _arenaThread.Start();
        }

        // 告诉游戏暂停
        public void Stop()
        {
            _stopRequested.Value = true;
        }

        // 在自己独立线程中运行
        // 实际游戏运行地方
        private void ArenaRun()
        {
            Console.WriteLine("[{0:000}] Waiting for players", Id);
            GameTime gameTime = new GameTime();

            // 在switch语句中使用的变量
            TimeSpan notifyGameStartTimeout = TimeSpan.FromSeconds(2.5);
            TimeSpan sendGameStateTimeout = TimeSpan.FromMilliseconds(1000f / 30f); // 多久更新用户

            // 循环
            bool running = true;
            bool playerDropped = false;
            while (running)
            {
                // 弹出消息（如果有的话）
                NetworkMessage message;
                bool haveMsg = _messages.TryDequeue(out message);

                switch (State.Value)
                {
                    case ArenaState.WaitingForPlayers:
                        if (haveMsg)
                        {
                            // 等待直到有两个用户加入进来
                            HandleConnectionSetup(LeftPlayer, message);
                            HandleConnectionSetup(RightPlayer, message);

                            // 检查我们是否准备好了
                            if (LeftPlayer.HavePaddle && RightPlayer.HavePaddle)
                            {
                                // 尝试立刻发送GameStart包
                                NotifyGameStart(LeftPlayer, new TimeSpan());
                                NotifyGameStart(RightPlayer, new TimeSpan());

                                // 转换状态
                                State.Value = ArenaState.NotifyingGameStart;
                            }
                        }
                        break;

                    case ArenaState.NotifyingGameStart:
                        // 尝试发生GameStart包
                        NotifyGameStart(LeftPlayer, notifyGameStartTimeout);
                        NotifyGameStart(RightPlayer, notifyGameStartTimeout);

                        // 检测ACK
                        if (haveMsg && (message.Packet.Type == PacketType.GameStartAck))
                        {
                            // 标记发送信息来的玩家为true
                            if (message.Sender.Equals(LeftPlayer.EndPoint))
                                LeftPlayer.Ready = true;
                            else if (message.Sender.Equals(RightPlayer.EndPoint))
                                RightPlayer.Ready = true;
                        }

                        // 我们准备好收发游戏数据了吗
                        if (LeftPlayer.Ready && RightPlayer.Ready)
                        {
                            // 初始化一些游戏对象的位置
                            _ball.Initialize();
                            LeftPlayer.Paddle.Initialize();
                            RightPlayer.Paddle.Initialize();

                            // 发送基础游戏状态
                            SendGameState(LeftPlayer, new TimeSpan());
                            SendGameState(RightPlayer, new TimeSpan());

                            // 开始游戏计时器
                            State.Value = ArenaState.InGame;
                            Console.WriteLine("[{0:000}] Starting Game", Id);
                            _gameTimer.Start();
                        }
                        break;

                    case ArenaState.InGame:
                        // 更新游戏计时器
                        TimeSpan now = _gameTimer.Elapsed;
                        gameTime = new GameTime(now, now - gameTime.TotalGameTime);

                        // 从客户端获取板的位置
                        if (haveMsg)
                        {
                            switch (message.Packet.Type)
                            {
                                case PacketType.PaddlePosition:
                                    HandlePaddleUpdate(message);
                                    break;

                                case PacketType.Heartbeat:
                                    // 用ACK回复
                                    HeartbeatAckPacket hap = new HeartbeatAckPacket();
                                    PlayerInfo player = message.Sender.Equals(LeftPlayer.EndPoint) ? LeftPlayer : RightPlayer;
                                    SendTo(player, hap);

                                    // 记录时间
                                    player.LastPacketReceivedTime = message.ReceiveTime;
                                    break;
                            }
                        }

                        // 更新游戏部分
                        _ball.ServerSideUpdate(gameTime);
                        CheckForBallCollistion();

                        // 发送数据
                        SendGameState(LeftPlayer, sendGameStateTimeout);
                        SendGameState(RightPlayer, sendGameStateTimeout);
                        break;
                }

                // 检测有无一方客户端退出
                if (haveMsg && (message.Packet.Type == PacketType.Bye))
                {
                    // 有一方退出了
                    PlayerInfo player = message.Sender.Equals(LeftPlayer.EndPoint) ? LeftPlayer : RightPlayer;
                    running = false;
                    Console.WriteLine("[{0:000}] Quit detected from {1} at {2}",
                        Id, player.Paddle.Side, _gameTimer.Elapsed);

                    // 告诉另一方
                    if (player.Paddle.Side == PaddleSide.Left)
                    {
                        // 左边离去，告诉右边
                        if (RightPlayer.IsSet)
                            _server.SendBye(RightPlayer.EndPoint);
                    }
                    else
                    {
                        // 右边离去，告诉左边
                        if (LeftPlayer.IsSet)
                            _server.SendBye(LeftPlayer.EndPoint);
                    }
                }

                // 检测超时
                playerDropped |= TimeOut(LeftPlayer);
                playerDropped |= TimeOut(RightPlayer);

                // 休息
                Thread.Sleep(1);

                // 检测是否退出
                running &= !_stopRequested.Value;
                running &= !playerDropped;
            }

            // 结束游戏
            _gameTimer.Stop();
            State.Value = ArenaState.GamwOver;
            Console.WriteLine("[{0:000}] Game Over, total game time was {1}", Id, _gameTimer.Elapsed);

            // 如果需要关闭，优雅的告诉客户端退出
            if (_stopRequested.Value)
            {
                Console.WriteLine("[{0:000}] Notifying Player of server shutdown", Id);

                if (LeftPlayer.IsSet)
                    _server.SendBye(LeftPlayer.EndPoint);
                if (RightPlayer.IsSet)
                    _server.SendBye(RightPlayer.EndPoint);
            }

            // 告诉服务器我们结束了
            _server.NotifyDone(this);
        }

        // 给底层的线程100毫秒结束时间
        public void JoinThread()
        {
            _arenaThread.Join(100);
        }

        // 服务器用来将消息分发给竞技场
        public void EnqueMessage(NetworkMessage nm)
        {
            _messages.Enqueue(nm);
        }

        // 给玩家发送一个包，并标记其他信息
        private void SendTo(PlayerInfo player, Packet packet)
        {
            _server.SendPacket(packet, player.EndPoint);
            player.LastPacketSentTime = DateTime.Now;
        }

        // 如果玩家超时返回true
        // 如果我们根本没有收到他们的心跳，则未超时
        private bool TimeOut(PlayerInfo player)
        {
            // 目前没有任何记录
            if (player.LastPacketReceivedTime == DateTime.MinValue)
                return false;

            // 计算
            bool timeoutDetected = (DateTime.Now > (player.LastPacketReceivedTime.Add(_heartbeatTimeout)));
            if (timeoutDetected)
                Console.WriteLine("[{0:000}] Timeout detected on {1} Player at {2}", Id, player.Paddle.Side, _gameTimer.Elapsed);

            return timeoutDetected;
        }

        private void HandleConnectionSetup(PlayerInfo player, NetworkMessage message)
        {
            // 确保信息是由正确的客户端提供的
            bool sendByPlayer = message.Sender.Equals(player.EndPoint);
            if (sendByPlayer)
            {
                // 记录我们最后一次获得消息的时间
                player.LastPacketReceivedTime = message.ReceiveTime;

                // 他们需要加入吗？还是心跳确认
                switch (message.Packet.Type)
                {
                    case PacketType.RequestJoin:
                        Console.WriteLine($"{Id} Join Request from {1}", player.EndPoint);
                        SendAcceptJoin(player);
                        break;

                    case PacketType.AcceptJoinAck:
                        // 他们知道了（他们会发送心跳直到游戏结束）
                        player.HavePaddle = true;
                        break;

                    case PacketType.Heartbeat:
                        // 他们等待游戏开始，回应ACK
                        HeartbeatAckPacket hap = new HeartbeatAckPacket();
                        SendTo(player, hap);

                        // 万一他们的ACK没有被我们获得
                        if (!player.HavePaddle)
                            SendAcceptJoin(player);

                        break;
                }
            }
        }

        // 给玩家发送AcceptJoinPacket
        public void SendAcceptJoin(PlayerInfo player)
        {
            // 他们需要知道自己是哪一边
            AcceptJoinPacket ajp = new AcceptJoinPacket();
            ajp.Side = player.Paddle.Side;
            SendTo(player, ajp);
        }

        // 尝试提醒其他的玩家游戏开始了
        // retryTimeout是重发数据报所需要的时间
        private void NotifyGameStart(PlayerInfo player, TimeSpan retryTimeout)
        {
            // 检测他们是否准备好了
            if (player.Ready)
                return;

            // 确保不给他们发送垃圾消息
            if (DateTime.Now >= (player.LastPacketSentTime.Add(retryTimeout)))
            {
                GameStartPacket gsp = new GameStartPacket();
                SendTo(player, gsp);
            }
        }

        // 发送当前游戏状态信息给玩家
        // resendTimeout是直到发送另一个GameStatePacket用了多久
        private void SendGameState(PlayerInfo player, TimeSpan resendTimeout)
        {
            if (DateTime.Now >= (player.LastPacketSentTime.Add(resendTimeout)))
            {
                // 设置数据
                GameStatePacket gsp = new GameStatePacket();
                gsp.LeftY = LeftPlayer.Paddle.Position.Y;
                gsp.RightY = RightPlayer.Paddle.Position.Y;
                gsp.BallPosition = _ball.Position;
                gsp.LeftScore = LeftPlayer.Paddle.Score;
                gsp.RightScore = RightPlayer.Paddle.Score;

                SendTo(player, gsp);
            }
        }

        // 告诉客户端双方打开音效
        private void PlaySoundEffect(string sfxName)
        {
            // 制作相关包
            PlaySoundEffectPacket packet = new PlaySoundEffectPacket();
            packet.SFXName = sfxName;

            SendTo(LeftPlayer, packet);
            SendTo(RightPlayer, packet);
        }

        //  处理球碰撞相关事情（包括得分）
        private void CheckForBallCollistion()
        {
            // 上/底部
            float ballY = _ball.Position.Y;
            if ((ballY <= _ball.TopmostY) || (ballY >= _ball.BottommostY))
            {
                _ball.Speed.Y *= -1;
                // PlaySoundEffect("ball-hit");
            }

            // 球碰到了左右边（得分）
            float ballX = _ball.Position.X;
            if (ballX <= _ball.LeftmostX)
            {
                // 右边玩家得分（重置球）
                RightPlayer.Paddle.Score += 1;
                Console.WriteLine($"{Id} Right Player scored ({1} -- {2}) at {3}",
                    LeftPlayer.Paddle.Score, RightPlayer.Paddle.Score, _gameTimer.Elapsed);
                _ball.Initialize();
                // PlaySoundEffect("score");
            }
            else if (ballX >= _ball.RightmostX)
            {
                // 左边玩家得分（重置球）
                LeftPlayer.Paddle.Score += 1;
                Console.WriteLine($"{Id} Right Player scored ({1} -- {2}) at {3}",
                    LeftPlayer.Paddle.Score, RightPlayer.Paddle.Score, _gameTimer.Elapsed);
                _ball.Initialize();
                // PlaySoundEffect("score");
            }

            // 球撞击到板上
            PaddleCollision collision;
            if (LeftPlayer.Paddle.Collides(_ball, out collision))
                ProcessBallHitWithPaddle(collision);
            if (RightPlayer.Paddle.Collides(_ball, out collision))
                ProcessBallHitWithPaddle(collision);
        }

        // 从客户端更新板的位置
        // `message.Packet.Type` 一定是 `PacketType.PaddlePosition`
        //TODO 增加一些作弊检测
        private void HandlePaddleUpdate(NetworkMessage message)
        {
            // 只有两个可能的玩家
            PlayerInfo player = message.Sender.Equals(LeftPlayer.EndPoint) ? LeftPlayer : RightPlayer;

            // 确保我们使用客户端发来的最新消息，否则忽略它
            if (message.Packet.Timestamp > player.LastPacketReceivedTimestamp)
            {
                // 记录时间和时间戳
                player.LastPacketReceivedTimestamp = message.Packet.Timestamp;
                player.LastPacketReceivedTime = message.ReceiveTime;

                // 获取包然后设置数据
                PaddlePositionPacket ppp = new PaddlePositionPacket(message.Packet.GetBytes());
                player.Paddle.Position.Y = ppp.Y;
            }
        }

        private void ProcessBallHitWithPaddle(PaddleCollision collision)
        {
            // 安全性检测
            if (collision == PaddleCollision.None)
                return;

            // 增加速度
            _ball.Speed.X *= _map((float)_random.NextDouble(), 0, 1, 1, 1.25f);
            _ball.Speed.Y *= _map((float)_random.NextDouble(), 0, 1, 1, 1.25f);

            // 在另一个方向弹出
            _ball.Speed.X *= -1;

            // 击中顶部或者底部
            if ((collision == PaddleCollision.WithTop) || (collision == PaddleCollision.WithBottom))
                _ball.Speed.Y *= -1;

            // 在客户端上开启音效
            PlaySoundEffect("ballHit");
        }

        // 小的帮助函数映射一个值的范围到另一个
        private float _map(float x, float a, float b, float p, float q)
        {
            return p + (x - 1) * (q - p) / (b - a);
        }
    }
}
