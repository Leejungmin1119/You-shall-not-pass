using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

// Mirror의 UDP LAN 검색으로 주소를 사용자에게 입력받지 않는다.
public class LanRoomConnector : MonoBehaviour
{
    private CapstoneRoomManager manager;
    private CapstoneLanDiscovery discovery;
    private readonly Dictionary<long, ServerResponse> rooms = new Dictionary<long, ServerResponse>();
    private readonly Dictionary<long, float> seen = new Dictionary<long, float>();
    private float searchStarted, firstFound, nextProbe;
    private string[] localIPv4Addresses = new string[0];
    public IReadOnlyList<string> LocalIPv4Addresses => localIPv4Addresses;
    public string LocalAddresses => localIPv4Addresses.Length > 0
        ? string.Join(" / ", localIPv4Addresses) : "IPv4 주소를 찾지 못했습니다.";
    public bool Advertising { get; private set; }
    public bool Searching { get; private set; }
    public ServerResponse[] Rooms => rooms.Values.OrderBy(r => r.serverId).ToArray();

    public void Initialize(CapstoneRoomManager owner, Transport lanTransport)
    {
        manager = owner;
        discovery = GetComponent<CapstoneLanDiscovery>() ?? gameObject.AddComponent<CapstoneLanDiscovery>();
        discovery.transport = lanTransport;
        // 프로젝트 구분 값. 보안 비밀번호가 아니다. 모든 빌드에서 같아야 한다.
        discovery.secretHandshake = 0x59534E505F4C414E;
        discovery.OnServerFound = new ServerFoundUnityEvent<ServerResponse>();
        discovery.OnServerFound.AddListener(OnFound);
    }

    /// <summary>접속이나 방 생성 없이 현재 유선/Wi-Fi IPv4를 읽는다.</summary>
    public void RefreshLocalAddresses()
    {
        try
        {
            localIPv4Addresses = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .OrderBy(n => n.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ||
                    n.NetworkInterfaceType == NetworkInterfaceType.Ethernet ? 0 : 1)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address))
                .Select(a => a.Address.ToString()).Distinct().Take(3).ToArray();
        }
        catch (Exception exception) when (exception is NetworkInformationException || exception is NotSupportedException)
        {
            localIPv4Addresses = new string[0];
        }
    }

    public void Advertise()
    {
        if (Advertising) return;
        RefreshLocalAddresses();
        try
        {
            discovery.AdvertiseServer(); Advertising = true;
            Debug.Log("[LAN] Room advertised on UDP 47777; game uses KCP UDP 7777.");
        }
        catch (Exception exception)
        {
            Advertising = false;
            Debug.LogError("[LAN] Could not advertise room: " + exception);
            manager.ReportConnectionFailure("LAN 방을 열지 못했습니다. 다른 게임 방과 포트 사용·로컬 네트워크 권한을 확인해 주세요.");
        }
    }

    public void Search()
    {
        Cancel(); rooms.Clear(); seen.Clear(); firstFound = float.PositiveInfinity;
        Searching = true; searchStarted = Time.unscaledTime;
        try
        {
            discovery.PrepareSearch();
            discovery.StartDiscovery();
            nextProbe = Time.unscaledTime;
        }
        catch (Exception exception)
        {
            Debug.LogError("[LAN] Search startup failed: " + exception);
            Cancel(); manager.ReportConnectionFailure("방 검색을 시작하지 못했습니다. 로컬 네트워크 권한을 확인해 주세요.");
        }
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
        if (now >= nextProbe)
        {
            nextProbe = now + 1;
            discovery.SendSearchProbes();
        }
        foreach (long id in seen.Where(p => now - p.Value > 8).Select(p => p.Key).ToArray())
        { seen.Remove(id); rooms.Remove(id); }
        if (rooms.Count == 1 && now - firstFound > 2)
        {
            Join(Rooms[0]);
            return;
        }
        if (rooms.Count == 0 && now - searchStarted > 15)
        {
            Cancel();
            manager.ReportConnectionFailure("방을 찾지 못했습니다. 같은 내부망인지 확인하거나 방장 IP로 직접 참가해 주세요.");
        }
    }

    public void Join(ServerResponse response)
    {
        if (!Searching || NetworkClient.active || NetworkServer.active) return;
        Cancel();
        Debug.Log("[LAN] Connecting to " + response.uri);
        manager.StartClient(response.uri);
    }

    /// <summary>자동 검색이 통과하지 않는 망에서도 도달 가능한 방장 IPv4로 접속한다.</summary>
    public void ConnectDirect(string address)
    {
        if (NetworkClient.active || NetworkServer.active || Searching) return;
        if (!TryNormalizeAddress(address, out string normalized))
        {
            manager.ReportConnectionFailure("올바른 방장 IPv4를 입력해 주세요. 예: 192.168.55.80");
            return;
        }
        Cancel();
        manager.networkAddress = normalized;
        manager.StartClient();
    }

    public static bool TryNormalizeAddress(string input, out string normalized)
    {
        normalized = null;
        string[] parts = (input ?? "").Trim().Split('.');
        if (parts.Length != 4) return false;
        var bytes = new byte[4];
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length == 0 || parts[i].Length > 3 || parts[i].Any(c => c < '0' || c > '9') ||
                !byte.TryParse(parts[i], out bytes[i])) return false;
        // DNS/URL/포트와 멀티캐스트·브로드캐스트·미지정 주소는 직접 접속 대상으로 받지 않는다.
        if (bytes[0] == 0 || bytes[0] >= 224) return false;
        normalized = new IPAddress(bytes).ToString();
        return true;
    }

    public void Cancel()
    {
        Searching = Advertising = false;
        discovery?.StopDiscovery();
    }

    private void Show(string message) => manager.GetComponent<CapstoneLobbyUI>()?.ShowNotice("WI-FI ROOM", message, false);
}
