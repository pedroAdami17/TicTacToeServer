using Unity.Networking.Transport;
using UnityEngine;

public class MessageProcessor
{
    private NetworkServer networkServer;

    public MessageProcessor(NetworkServer server)
    {
        networkServer = server;
    }

    public void ProcessMessage(string msg, NetworkConnection clientConnection)
    {
        Debug.Log("Msg received = " + msg);

        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        switch (signifier)
        {
            case ClientToServerSignifiers.Register:
                networkServer.ProcessRegister(csv, clientConnection);
                break;
            case ClientToServerSignifiers.Login:
                networkServer.ProcessLogin(csv, clientConnection);
                break;
            case ClientToServerSignifiers.JoinQueue:
                networkServer.ProcessJoinQueue(clientConnection);
                break;
            case ClientToServerSignifiers.MakeMove:
                networkServer.ProcessMakeMove(csv, clientConnection);
                break;
            case ClientToServerSignifiers.GameRestart:
                networkServer.ProcessGameRestart(clientConnection);
                break;
            // Handle other cases as needed
            default:
                Debug.LogError($"Unknown message signifier: {signifier}");
                break;
        }
    }
}

public static class ClientToServerSignifiers
{
    public const int Register = 1;
    public const int Login = 2;
    public const int JoinQueue = 3;
    public const int MakeMove = 4;
    public const int GameRestart = 5;
    public const int QuitGame = 6;
}

public static class ServerToClientSignifiers
{
    public const int LoginComplete = 1;
    public const int LoginFailed = 2;
    public const int RegisterComplete = 3;
    public const int RegisterFailed = 4;
    public const int GameStart = 5;
    public const int UpdateGameBoard = 6;
    public const int GameReset = 7;
    public const int PlayerDisconnected = 8;
    public const int Observer = 9;
}