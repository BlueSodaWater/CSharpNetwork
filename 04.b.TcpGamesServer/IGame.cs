using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace TcpGames
{
    interface IGame
    {
        // 游戏名
        string Name { get; }

        // 需要几个玩家开始
        int RequiredPlayers { get; }

        // 游戏增加新玩家（需要在游戏开始前）
        bool AddPlayer(TcpClient player);

        // 告诉服务器断开某个玩家的连接
        void DisconnectClient(TcpClient client);

        // 游戏主循环
        void Run();
    }
}
