using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Concurrent;
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
        private ThreadSafe<bool> _runninf = new ThreadSafe<bool>(false);
        private ThreadSafe<bool> _sendBye = new ThreadSafe<bool>(false);

        static void Main(string[] args)
        {
            Console.WriteLine("Hello World!");
        }
    }
}
