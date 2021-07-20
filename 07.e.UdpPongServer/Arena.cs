using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
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

        private PongServer pongServer;

        public Arena(PongServer pongServer)
        {
            this.pongServer = pongServer;
        }

        internal void Stop()
        {
            throw new NotImplementedException();
        }

        internal void Start()
        {
            throw new NotImplementedException();
        }

        internal bool TryAddPlayer(IPEndPoint sender)
        {
            throw new NotImplementedException();
        }

        internal void EnqueMessage(NetworkMessage nm)
        {
            throw new NotImplementedException();
        }

        internal void JoinThread()
        {
            throw new NotImplementedException();
        }
    }
}
