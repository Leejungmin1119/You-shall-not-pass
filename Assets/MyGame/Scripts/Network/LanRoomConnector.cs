using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

// Mirror의 UDP LAN 검색으로 주소를 사용자에게 입력받지 않는다.
public class LanRoomConnector : MonoBehaviour
{
    private CapstoneRoomManager manager;
    private NetworkDiscovery discovery;
    private readonly Dictionary<long, ServerResponse> rooms = new Dictionary<long, ServerResponse>();
    private readonly Dictionary<long, float> seen = new Dictionary<long, float>();
    private float searchStarted, firstFound;
    public bool Searching { get; private set; }
    public ServerResponse[] Rooms => rooms.Values.OrderBy(r => r.serverId).ToArray();

    public void Initialize(CapstoneRoomManager owner, Transport lanTransport)
    {
        manager = owner;
        discovery = GetComponent<NetworkDiscovery>() ?? gameObject.AddComponent<NetworkDiscovery>();
        discovery.transport = lanTransport;
        // 프로젝트 구분 값. 보안 비밀번호가 아니다. 모든 빌드에서 같아야 한다.
        discovery.secretHandshake = 0x59534E505F4C414E;
        discovery.OnServerFound = new ServerFoundUnityEvent<ServerResponse>();
        discovery.OnServerFound.AddListener(OnFound);
    }

    public void Advertise()
    {
        try { discovery.AdvertiseServer(); }
        catch (Exception) { Show("Could not advertise the Wi-Fi room. Check firewall permissions."); }
    }

    public void Search()
    {
        Cancel(); rooms.Clear(); seen.Clear(); firstFound = float.PositiveInfinity;
        Searching = true; searchStarted = Time.unscaledTime;
        try { discovery.StartDiscovery(); }
        catch (Exception) { Cancel(); Show("Wi-Fi room search is unavailable. Check network permissions."); }
    }

    private void OnFound(ServerResponse response)
    {
        if (!Searching || NetworkClient.active || NetworkServer.active || response.uri == null) return;
        if (rooms.Count == 0) firstFound = Time.unscaledTime;
        rooms[response.serverId] = response;
        seen[response.serverId] = Time.unscaledTime;
    }

    private void Update()
    {
        if (!Searching) return;
        float now = Time.unscaledTime;
        foreach (long id in seen.Where(p => now - p.Value > 8).Select(p => p.Key).ToArray())
        { seen.Remove(id); rooms.Remove(id); }
        if (rooms.Count == 1 && now - firstFound > 2)
        {
            Join(Rooms[0]);
            return;
        }
        if (rooms.Count == 0 && now - searchStarted > 12)
        {
            Cancel();
            Show("No room found. Ask the host to create one on the same Wi-Fi.");
        }
    }

    public void Join(ServerResponse response)
    {
        if (!Searching || NetworkClient.active || NetworkServer.active) return;
        Cancel();
        manager.StartClient(response.uri);
    }

    public void Cancel()
    {
        Searching = false;
        discovery?.StopDiscovery();
    }

    private void Show(string message) => manager.GetComponent<CapstoneLobbyUI>()?.ShowNotice("WI-FI ROOM", message, false);
}
