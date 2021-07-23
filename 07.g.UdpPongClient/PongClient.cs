using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace PongGame
{
    public enum ClientState
    {
        NotConnected,
        EstablishingConnection,
        WaitingForGameStart,
        InGame,
        GameOver,
    }

    class PongClient : Game
    {
        // 游戏相关
        private GraphicsDeviceManager _graphics;
        private SpriteBatch _spriteBatch;

        // 网络相关
        private UdpClient _udpClient;
        public readonly string ServerHostname;
        public readonly int ServerPort;

        // 时间测量
        private DateTime _lastPacketReceivedTime = DateTime.MinValue; // 从客户端时间
        private DateTime _lastPacketSentTime = DateTime.MinValue; // 从客户端时间
        private long _lastPacketReceivedTimestamp = 0; //从服务器时间
        private TimeSpan _heartbeatTimeout = TimeSpan.FromSeconds(20);
        private TimeSpan _sendPaddlePositionTimeout = TimeSpan.FromMilliseconds(1000f / 30f); // 多久更新服务器信息

        // 消息
        private Thread _networkThread;
        private ConcurrentQueue<NetworkMessage> _incomingMessages = new ConcurrentQueue<NetworkMessage>();
        private ConcurrentQueue<Packet> _outgoingMessages = new ConcurrentQueue<Packet>();

        // 游戏对象
        private Ball _ball;
        private Paddle _left;
        private Paddle _right;
        private Paddle _ourPaddle;
        private float _previousY;

        // 玩家的信息
        private Texture2D _establishingConnectionMsg;
        private Texture2D _waitingForGameStartMsg;
        private Texture2D _gameOverMsg;

        // 音频
        private SoundEffect _ballHitSFX;
        private SoundEffect _scoreSFX;

        // 状态相关
        private ClientState _state = ClientState.NotConnected;
        private ThreadSafe<bool> _running = new ThreadSafe<bool>(false);
        private ThreadSafe<bool> _sendBye = new ThreadSafe<bool>(false);

        public PongClient(string hostname, int port)
        {
            // 内容
            Content.RootDirectory = "Content";

            // 图形设置
            _graphics = new GraphicsDeviceManager(this);
            _graphics.PreferredBackBufferWidth = GameGeometry.PlayArea.X;
            _graphics.PreferredBackBufferHeight = GameGeometry.PlayArea.Y;
            _graphics.IsFullScreen = false;
            _graphics.ApplyChanges();

            // 游戏对象
            _ball = new Ball();
            _left = new Paddle(PaddleSide.Left);
            _right = new Paddle(PaddleSide.Right);

            // 连接相关
            ServerHostname = hostname;
            ServerPort = port;
            _udpClient = new UdpClient(ServerHostname, ServerPort);
        }

        protected override void Initialize()
        {
            base.Initialize();
            _left.Initialize();
            _right.Initialize();
        }

        protected override void LoadContent()
        {
            _spriteBatch = new SpriteBatch(_graphics.GraphicsDevice);

            // 加载游戏对象
            _ball.LoadContent(Content);
            _left.LoadContent(Content);
            _right.LoadContent(Content);

            // 加载消息
            _establishingConnectionMsg = Content.Load<Texture2D>("establishing-connection-msg");
            _waitingForGameStartMsg = Content.Load<Texture2D>("waiting-for-game-start-msg");
            _gameOverMsg = Content.Load<Texture2D>("game-over-msg");

            // 记载音效
        }

        protected override void UnloadContent()
        {
            // 清除
            _networkThread?.Join(TimeSpan.FromSeconds(2));
            _udpClient.Close();

            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            // 关闭连接
            KeyboardState kbs = Keyboard.GetState();
            if (kbs.IsKeyDown(Keys.Escape))
            {
                // 玩家想要推出，发送一个ByePacket（如果我们连接上了）
                if ((_state == ClientState.EstablishingConnection) ||
                    (_state == ClientState.WaitingForGameStart) ||
                    (_state == ClientState.InGame))
                {
                    // 将会触发网络线程来发送Bye Packet
                    _sendBye.Value = true;
                }

                // 将会停止网络线程
                _running.Value = false;
                _state = ClientState.GameOver;
                Exit();
            }

            // 检测服务器超时
            if (TimeOut())
            {
                _state = ClientState.GameOver;
            }

            // 获取消息
            NetworkMessage message;
            bool haveMsg = _incomingMessages.TryDequeue(out message);

            // 检测服务器是否发送Bye消息
            if (haveMsg && (message.Packet.Type == PacketType.Bye))
            {
                // 关闭网络线程（不再需要了）
                _running.Value = false;
                _state = ClientState.GameOver;
            }

            switch (_state)
            {
                case ClientState.EstablishingConnection:
                    SendRequestJoin(TimeSpan.FromSeconds(1));
                    if (haveMsg)
                        HandleConnectionSetupResponse(message.Packet);
                    break;

                case ClientState.WaitingForGameStart:
                    // 发送心跳
                    SendHeartbeat(TimeSpan.FromSeconds(0.2));

                    if (haveMsg)
                    {
                        switch (message.Packet.Type)
                        {
                            case PacketType.AcceptJoin:
                                // 有可能他们在前一个状态没有收到我们的确认
                                SendAcceptJoinAck();
                                break;

                            case PacketType.HeartbeatAck:
                                // 记录ACK的时间
                                _lastPacketReceivedTime = message.ReceiveTime;
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;
                                break;

                            case PacketType.GameStart:
                                // 开始游戏并且确认
                                SendGameStartAck();
                                _state = ClientState.InGame;
                                break;
                        }
                    }
                    break;

                case ClientState.InGame:
                    // 发送消息
                    SendHeartbeat(TimeSpan.FromSeconds(0.2));

                    // 更新我们的板
                    _previousY = _ourPaddle.Position.Y;
                    _ourPaddle.ClientSideUpdate(gameTime);
                    SendPaddlePosition(_sendPaddlePositionTimeout);

                    if (haveMsg)
                    {
                        switch (message.Packet.Type)
                        {
                            case PacketType.GameStart:
                                // 有可能他们在前一个状态没有收到我们的确认
                                SendGameStartAck();
                                break;

                            case PacketType.HeartbeatAck:
                                // 记录确认时间
                                _lastPacketReceivedTime = message.ReceiveTime;
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;
                                break;

                            case PacketType.GameState:
                                // 更新游戏状态，确保它是最新的
                                if (message.Packet.Timestamp > _lastPacketReceivedTimestamp)
                                {
                                    _lastPacketReceivedTimestamp = message.Packet.Timestamp;

                                    GameStatePacket gsp = new GameStatePacket(message.Packet.GetBytes());
                                    _left.Score = gsp.LeftScore;
                                    _right.Score = gsp.RightScore;
                                    _ball.Position = gsp.BallPosition;

                                    // 更新另一块板
                                    if (_ourPaddle.Side == PaddleSide.Left)
                                        _right.Position.Y = gsp.RightY;
                                    else
                                        _left.Position.Y = gsp.LeftY;
                                }

                                break;

                            case PacketType.PlaySoundEffect:
                                // 开启声音
                                PlaySoundEffectPacket psep = new PlaySoundEffectPacket(message.Packet.GetBytes());
                                if (psep.SFXName == "ball-hit")
                                    _ballHitSFX.Play();
                                else if (psep.SFXName == "score")
                                    _scoreSFX.Play();

                                break;
                        }
                    }
                    break;

                case ClientState.GameOver:
                    // 这里游戏就结束了
                    break;
            }

            base.Update(gameTime);
        }

        public void Start()
        {
            _running.Value = true;
            _state = ClientState.EstablishingConnection;

            // 开启收/发包线程
            _networkThread = new Thread(new ThreadStart(NetworkRun));
            _networkThread.Start();
        }

        protected override void Draw(GameTime gameTime)
        {
            _graphics.GraphicsDevice.Clear(Color.Black);

            _spriteBatch.Begin();

            // 根据不同的状态画不同的东西
            switch (_state)
            {
                case ClientState.EstablishingConnection:
                    DrawCentered(_establishingConnectionMsg);
                    Window.Title = String.Format("Pong -- Connecting to {0}:{1}", ServerHostname, ServerPort);
                    break;

                case ClientState.WaitingForGameStart:
                    DrawCentered(_waitingForGameStartMsg);
                    Window.Title = String.Format("Pong -- Waiting for 2nd Player");
                    break;

                case ClientState.InGame:
                    // 画游戏对象
                    _ball.Draw(gameTime, _spriteBatch);
                    _left.Draw(gameTime, _spriteBatch);
                    _right.Draw(gameTime, _spriteBatch);

                    // 更改窗口标题
                    UpdateWindowTitleWithScore();
                    break;

                case ClientState.GameOver:
                    DrawCentered(_gameOverMsg);
                    UpdateWindowTitleWithScore();
                    break;
            }

            _spriteBatch.End();

            base.Draw(gameTime);
        }

        private void DrawCentered(Texture2D texture)
        {
            Vector2 textureCenter = new Vector2(texture.Width / 2, texture.Height / 2);
            _spriteBatch.Draw(texture, GameGeometry.ScreenCenter, Color.White);
        }

        private void UpdateWindowTitleWithScore()
        {
            string fmt = (_ourPaddle.Side == PaddleSide.Left) ?
                "[{0}] -- Pong -- {1}" : "{0} -- Pong -- [{1}]";
            Window.Title = String.Format(fmt, _left.Score, _right.Score);
        }


        private void NetworkRun()
        {
            while (_running.Value)
            {
                bool canRead = _udpClient.Available > 0;
                int numToWrite = _outgoingMessages.Count;

                // 如果有数据就去读取
                if (canRead)
                {
                    // 读取一个数据报
                    IPEndPoint ep = new IPEndPoint(IPAddress.Any, 0);
                    byte[] data = _udpClient.Receive(ref ep); // 阻塞

                    // 新消息加入队列
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
                    Packet packet;
                    bool have = _outgoingMessages.TryDequeue(out packet);
                    if (have)
                        packet.Send(_udpClient);
                }

                // 如果无事发生，休息一下
                if (!canRead && (numToWrite == 0))
                    Thread.Sleep(1);
            }

            // 检测是否被要求bye，最后一个操作
            if (_sendBye.Value)
            {
                ByePacket bp = new ByePacket();
                bp.Send(_udpClient);
                Thread.Sleep(1000); // 需要一些时间来发送
            }
        }

        // 排队给服务器发送单个包
        private void SendPacket(Packet packet)
        {
            _outgoingMessages.Enqueue(packet);
            _lastPacketSentTime = DateTime.Now;
        }

        // 发送RequestJoinPacket
        private void SendRequestJoin(TimeSpan retryTimeout)
        {
            // 确保不要发生垃圾信息
            if (DateTime.Now >= (_lastPacketSentTime.Add(retryTimeout)))
            {
                RequestJoinPacket gsp = new RequestJoinPacket();
                SendPacket(gsp);
            }
        }

        // 确认AcceptJinPacket
        private void SendAcceptJoinAck()
        {
            AcceptJoinAckPacket ajap = new AcceptJoinAckPacket();
            SendPacket(ajap);
        }

        // 当我们和服务器建立连接的时候回复包
        private void HandleConnectionSetupResponse(Packet packet)
        {
            // 检测接受和确认
            if (packet.Type == PacketType.AcceptJoin)
            {
                // 确保我们之前没有获得他
                if (_ourPaddle == null)
                {
                    // 看看我们是哪一边的板
                    AcceptJoinPacket ajp = new AcceptJoinPacket(packet.GetBytes());
                    if (ajp.Side == PaddleSide.Left)
                        _ourPaddle = _left;
                    else if (ajp.Side == PaddleSide.Right)
                        _ourPaddle = _right;
                    else
                        throw new Exception("Error, invalid paddle side given by server."); // 应该基本不会执行到这个，但是以防万一
                }

                // 发送回复
                SendAcceptJoinAck();

                // 更改状态
                _state = ClientState.WaitingForGameStart;
            }
        }

        // 给服务器发送HeartbeatPacket
        private void SendHeartbeat(TimeSpan resendTimeout)
        {
            // 确保不要发生垃圾信息
            if (DateTime.Now >= (_lastPacketSentTime.Add(resendTimeout)))
            {
                HeartbeatPacket hp = new HeartbeatPacket();
                SendPacket(hp);
            }
        }

        // 确认GameStartpACKET
        private void SendGameStartAck()
        {
            GameStartAckPacket gsap = new GameStartAckPacket();
            SendPacket(gsap);
        }

        // 给服务器发送我们板的Y轴位置（如果改变了的话）
        private void SendPaddlePosition(TimeSpan resendTimeout)
        {
            // 如果没变化就不要发送消息
            if (_previousY == _ourPaddle.Position.Y)
                return;

            // 确保不要发生垃圾信息
            if (DateTime.Now >= (_lastPacketSentTime.Add(resendTimeout)))
            {
                PaddlePositionPacket ppp = new PaddlePositionPacket();
                ppp.Y = _ourPaddle.Position.Y;

                SendPacket(ppp);
            }
        }

        // 如果服务器连接超时就返回true
        // 如果我们根本没有收到他们的包，则未超时
        private bool TimeOut()
        {
            // 目前还没有获得记录
            if (_lastPacketReceivedTime == DateTime.MinValue)
                return false;

            // 判断
            return (DateTime.Now > (_lastPacketReceivedTime.Add(_heartbeatTimeout)));
        }

        public static void Main(string[] args)
        {
            // 获取参数
            string hostname = "localhost";//args[0].Trim();
            int port = 6000;//int.Parse(args[1].Trim());

            // 开启客户端
            PongClient client = new PongClient(hostname, port);
            client.Start();
            client.Run();
        }
    }
}
