using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

public class PracticeNetworkManager : NetworkManager
{
    [SerializeField] private string gameSceneName = "GameScene";
    public bool IsLobby => UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == "LobbyScene";
    public override void OnStartServer()
    {
        base.OnStartServer();
        Debug.Log("서버에 접속했습니다.");
    }

    public override void OnStartClient()
    {
        base.OnStartClient();   
        Debug.Log("클라이언트가 시작되었습니다.");
    }

    public override void OnServerConnect(NetworkConnectionToClient connection)
    {
        base.OnServerConnect(connection);
        Debug.Log($"서버: 클라이언트가 접속했습니다. ID = {connection.connectionId}");
    }

    public override void OnClientConnect()
    {
        base.OnClientConnect();
        Debug.Log("클라이언트: 서버 접속에 성공했습니다.");
    }
    public bool CanStartGame()
    {
        if (!NetworkServer.active || NetworkServer.connections.Count != 2) return false;
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection.identity == null) return false;
            var lobbyPlayer = connection.identity.GetComponent<NetworkPlayerLobby>();
            if (lobbyPlayer == null || !lobbyPlayer.IsReady) return false;
        }
        return true;
    }

    [Server]
    public void TryStartGame()
    {
        if (networkSceneName == gameSceneName || NetworkServer.isLoadingScene) return;
        if (!CanStartGame())
        {
            GetComponent<CapstoneLobbyUI>()?.ShowNotice(
                "READY UP!", "Waiting for players and everyone to be ready.", false);
            return;
        }

        ServerChangeScene(gameSceneName);
    }

    // 각 클라이언트가 게임 씬 로딩을 마친 뒤 자기 화면에 표시한다.
    public override void OnClientSceneChanged()
    {
        base.OnClientSceneChanged();
        if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == gameSceneName)
            GetComponent<CapstoneLobbyUI>()?.ShowNotice(
                "GAME START!", "You shall not pass. Good luck!", true);
    }

    public override void Update()
    {
        base.Update();
        if (!NetworkServer.active || !NetworkClient.active || Keyboard.current == null) return;
        if (Keyboard.current.enterKey.wasPressedThisFrame) TryStartGame();
    }
}
