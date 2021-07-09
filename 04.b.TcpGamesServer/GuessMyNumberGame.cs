using System.Net.Sockets;

namespace TcpGames
{
    public class GuessMyNumberGame : IGame
    {
        public GuessMyNumberGame(TcpGamesServer tcpGamesServer)
        {
        }

        public string Name => throw new System.NotImplementedException();

        public int RequiredPlayers => throw new System.NotImplementedException();

        public bool AddPlayer(TcpClient player)
        {
            throw new System.NotImplementedException();
        }

        public void DisconnectClient(TcpClient client)
        {
            throw new System.NotImplementedException();
        }

        public void Run()
        {
            throw new System.NotImplementedException();
        }
    }
}