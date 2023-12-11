using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Networking.Transport;
using UnityEngine;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver networkDriver;
    private NativeList<NetworkConnection> networkConnections;

    NetworkPipeline reliableAndInOrderPipeline;
    NetworkPipeline nonReliableNotInOrderedPipeline;

    const ushort NetworkPort = 9001;

    const int MaxNumberOfClientConnections = 1000;

    LinkedList<PlayerAccount> playerAccounts;
    const int PlayerAccountInfo = 1;

    string PlayerAccountFilePath;

    private NetworkConnection playerInQueueID;

    LinkedList<GameRoom> gameRooms;

    private List<NetworkConnection> playerConnections = new List<NetworkConnection>();
    private char[] gameBoard = new char[9];

    private MessageProcessor messageProcessor;

    void Start()
    {
        networkDriver = NetworkDriver.Create();
        reliableAndInOrderPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage), typeof(ReliableSequencedPipelineStage));
        nonReliableNotInOrderedPipeline = networkDriver.CreatePipeline(typeof(FragmentationPipelineStage));
        NetworkEndPoint endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = NetworkPort;

        int error = networkDriver.Bind(endpoint);
        if (error != 0)
            Debug.Log("Failed to bind to port " + NetworkPort);
        else
            networkDriver.Listen();

        networkConnections = new NativeList<NetworkConnection>(MaxNumberOfClientConnections, Allocator.Persistent);

        PlayerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccounts.txt";
        playerAccounts = new LinkedList<PlayerAccount>();
        LoadPlayerAccounts();

        gameRooms = new LinkedList<GameRoom>();
        InitializeGameBoard();

        messageProcessor = new MessageProcessor(this);
    }

    private void InitializeGameBoard()
    {
        for (int i = 0; i < 9; i++)
        {
            gameBoard[i] = ' ';
        }
    }

    void OnDestroy()
    {
        networkDriver.Dispose();
        networkConnections.Dispose();
    }

    void Update()
    {
        #region Check Input and Send Msg

        if (Input.GetKeyDown(KeyCode.A))
        {
            for (int i = 0; i < networkConnections.Length; i++)
            {
                SendMessageToClient("Hello client's world, sincerely your network server", networkConnections[i]);
            }
        }

        #endregion

        networkDriver.ScheduleUpdate().Complete();

        #region Remove Unused Connections

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
            {
                networkConnections.RemoveAtSwapBack(i);
                i--;
            }
        }

        #endregion

        #region Accept New Connections

        while (AcceptIncomingConnection())
        {
            Debug.Log("Accepted a client connection");
        }

        #endregion

        #region Manage Network Events

        DataStreamReader streamReader;
        NetworkPipeline pipelineUsedToSendEvent;
        NetworkEvent.Type networkEventType;

        for (int i = 0; i < networkConnections.Length; i++)
        {
            if (!networkConnections[i].IsCreated)
                continue;

            while (PopNetworkEventAndCheckForData(networkConnections[i], out networkEventType, out streamReader, out pipelineUsedToSendEvent))
            {
                if (pipelineUsedToSendEvent == reliableAndInOrderPipeline)
                    Debug.Log("Network event from: reliableAndInOrderPipeline");
                else if (pipelineUsedToSendEvent == nonReliableNotInOrderedPipeline)
                    Debug.Log("Network event from: nonReliableNotInOrderedPipeline");

                switch (networkEventType)
                {
                    case NetworkEvent.Type.Data:
                        int sizeOfDataBuffer = streamReader.ReadInt();
                        NativeArray<byte> buffer = new NativeArray<byte>(sizeOfDataBuffer, Allocator.Persistent);
                        streamReader.ReadBytes(buffer);
                        byte[] byteBuffer = buffer.ToArray();
                        string msg = Encoding.Unicode.GetString(byteBuffer);
                        ProcessReceivedMsg(msg, networkConnections[i]);
                        buffer.Dispose();
                        break;
                    case NetworkEvent.Type.Disconnect:
                        Debug.Log("Client has disconnected from server");
                        networkConnections[i] = default(NetworkConnection);
                        break;
                }
            }
        }

        #endregion
    }

    private bool AcceptIncomingConnection()
    {
        NetworkConnection connection = networkDriver.Accept();
        if (connection == default(NetworkConnection))
            return false;

        networkConnections.Add(connection);
        return true;
    }

    private bool PopNetworkEventAndCheckForData(NetworkConnection networkConnection, out NetworkEvent.Type networkEventType, out DataStreamReader streamReader, out NetworkPipeline pipelineUsedToSendEvent)
    {
        networkEventType = networkConnection.PopEvent(networkDriver, out streamReader, out pipelineUsedToSendEvent);

        if (networkEventType == NetworkEvent.Type.Empty)
            return false;
        return true;
    }

    private void ProcessReceivedMsg(string msg, NetworkConnection clientConnection)
    {
        Debug.Log("Msg received = " + msg);
        messageProcessor.ProcessMessage(msg, clientConnection);

    }

    public void ProcessRegister(string[] csv, NetworkConnection clientConnection)
    {
        Debug.Log("create new account");

        string n = csv[1];
        string p = csv[2];
        bool nameIsInUse = false;

        foreach (PlayerAccount pa in playerAccounts)
        {
            if (pa.name == n)
            {
                nameIsInUse = true;
                break;
            }
        }

        if (nameIsInUse)
        {
            SendMessageToClient(ServerToClientSignifiers.RegisterFailed + "", clientConnection);
        }
        else
        {
            PlayerAccount newPlayerAccount = new PlayerAccount(n, p);
            playerAccounts.AddLast(newPlayerAccount);
            SendMessageToClient(ServerToClientSignifiers.RegisterComplete + "", clientConnection);

            SavePlayerAccounts();
        }
    }

    public void ProcessLogin(string[] csv, NetworkConnection clientConnection)
    {
        Debug.Log("login to accounnt");

        string n = csv[1];
        string p = csv[2];
        bool nameWasFound = false;
        bool msgSentToClient = false;

        foreach (PlayerAccount pa in playerAccounts)
        {
            if (pa.name == n)
            {
                nameWasFound = true;
                if (pa.password == p)
                {
                    SendMessageToClient(ServerToClientSignifiers.LoginComplete + "", clientConnection);
                    msgSentToClient = true;
                }
                else
                {
                    SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", clientConnection);
                    msgSentToClient = true;
                }
            }
        }
        if (!nameWasFound)
        {
            if (!msgSentToClient)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginFailed + "", clientConnection);
            }
        }
    }

    public void ProcessJoinQueue(NetworkConnection clientConnection)
    {
        if (playerInQueueID == null)
        {
            playerInQueueID = clientConnection;
        }
        else
        {
            GameRoom gr = new GameRoom(playerInQueueID, clientConnection);
            gameRooms.AddLast(gr);

            SendMessageToClient(ServerToClientSignifiers.GameStart + ",", gr.PlayerID1);
            SendMessageToClient(ServerToClientSignifiers.GameStart + ",", gr.PlayerID2);

            playerInQueueID = default(NetworkConnection);

            if (gameRooms.Count > 0)
            {
                NetworkConnection observerConnection = clientConnection; // Change this to the appropriate observer connection
                gameRooms.Last.Value.AddObserver(observerConnection);
                SendMessageToClient(ServerToClientSignifiers.Observer + ",", observerConnection);
            }
        }
    }

    public void ProcessMakeMove(string[] csv, NetworkConnection clientConnection)
    {
        int position;
        char playerSymbol;

        if (int.TryParse(csv[1], out position) && csv[2].Length == 1)
        {
            playerSymbol = csv[2][0];

            UpdateGameBoard(position, playerSymbol);
            BroadcastGameBoard(position, playerSymbol);
        }
        else
        {
            Debug.LogError("Invalid format for MakeMove message.");
        }
    }

    public void ProcessGameRestart(NetworkConnection clientConnection)
    {
        InitializeGameBoard();

        string message = ServerToClientSignifiers.GameReset + ",";
        BroadcastToClients(message);
    }

    private void UpdateGameBoard(int position, char player)
    {
        if (position >= 0 && position < 9)
        {
            gameBoard[position] = player;
        }
        else
        {
            Debug.LogError("Invalid move position");
        }
    }

    private void BroadcastGameBoard(int position, char playerSymbol)
    {
        string message = $"{ServerToClientSignifiers.UpdateGameBoard},{position},{playerSymbol}";

        Debug.Log($"Broadcasting updated game board: Position {position}, Symbol {playerSymbol}");

        BroadcastToClients(message);
    }
    private void BroadcastToClients(string msg)
    {
        // Broadcast the message to all clients
        for (int i = 0; i < networkConnections.Length; i++)
        {
            SendMessageToClient(msg, networkConnections[i]);
        }
    }

    private void SavePlayerAccounts()
    {
        StreamWriter sw = new StreamWriter(PlayerAccountFilePath);

        foreach (PlayerAccount pa in playerAccounts)
        {
            sw.WriteLine(PlayerAccountInfo + "," + pa.name + "," + pa.password);
        }
        sw.Close();
    }

    private void LoadPlayerAccounts()
    {
        if (File.Exists(PlayerAccountFilePath))
        {
            StreamReader sr = new StreamReader(PlayerAccountFilePath);
            string line;

            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                int signifier = int.Parse(csv[0]);

                if (signifier == PlayerAccountInfo)
                {
                    PlayerAccount pa = new PlayerAccount(csv[1], csv[2]);
                    playerAccounts.AddLast(pa);
                }
            }

            sr.Close();
        }

    }

    public void SendMessageToClient(string msg, NetworkConnection networkConnection)
    {
        byte[] msgAsByteArray = Encoding.Unicode.GetBytes(msg);
        NativeArray<byte> buffer = new NativeArray<byte>(msgAsByteArray, Allocator.Persistent);


        //Driver.BeginSend(m_Connection, out var writer);
        DataStreamWriter streamWriter;
        //networkConnection.
        networkDriver.BeginSend(reliableAndInOrderPipeline, networkConnection, out streamWriter);
        streamWriter.WriteInt(buffer.Length);
        streamWriter.WriteBytes(buffer);
        networkDriver.EndSend(streamWriter);

        buffer.Dispose();
    }


}

public class PlayerAccount
{
    public string name, password;

    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}


public class GameRoom
{
    public NetworkConnection PlayerID1, PlayerID2;
    private List<NetworkConnection> observers;

    public GameRoom(NetworkConnection playerID1, NetworkConnection playerID2)
    {
        PlayerID1 = playerID1;
        PlayerID2 = playerID2;
        observers = new List<NetworkConnection>();
    }

    public void AddObserver(NetworkConnection observer)
    {
        observers.Add(observer);
    }
}




