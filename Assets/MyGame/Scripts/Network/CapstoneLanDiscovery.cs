using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Mirror;
using Mirror.Discovery;
using UnityEngine;

// 전체 브로드캐스트가 다른 어댑터로 나가는 경우를 위해
// 현재 IPv4 인터페이스별 대역 브로드캐스트와 loopback도 조회한다.
public class CapstoneLanDiscovery : NetworkDiscovery
{
    private readonly HashSet<IPAddress> destinations = new HashSet<IPAddress>();
    public string LastError { get; private set; }
    private long instanceId;

    // 방 광고는 매니저가 Host를 연 뒤 명시적으로 시작한다.
    // 기본 Start의 headless 자동 광고는 테스트/클라이언트에서 포트를 선점한다.
    public override void Start() => instanceId = RandomLong();

    public void PrepareSearch()
    {
        enableActiveDiscovery = false;
        destinations.Clear();
        destinations.Add(IPAddress.Broadcast);
        destinations.Add(IPAddress.Loopback);
        try
        {
            foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (UnicastIPAddressInformation info in network.GetIPProperties().UnicastAddresses)
                {
                    if (info.Address.AddressFamily != AddressFamily.InterNetwork || info.IPv4Mask == null) continue;
                    byte[] address = info.Address.GetAddressBytes(), mask = info.IPv4Mask.GetAddressBytes();
                    var broadcast = new byte[4];
                    for (int i = 0; i < 4; i++) broadcast[i] = (byte)(address[i] | (byte)~mask[i]);
                    destinations.Add(new IPAddress(broadcast));
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[LAN] Interface enumeration: " + exception.Message);
        }
        LastError = null;
        Debug.Log("[LAN] Searching UDP " + serverBroadcastListenPort + " via " + string.Join(", ", destinations));
    }

    public void SendSearchProbes()
    {
        if (clientUdpClient == null) return;
        // 송신이 끝나기 전에 pooled writer를 반환하지 않는다.
        using (NetworkWriterPooled writer = NetworkWriterPool.Get())
        {
            writer.WriteLong(secretHandshake);
            writer.Write(new ServerRequest());
            ArraySegment<byte> data = writer.ToArraySegment();
            foreach (IPAddress address in destinations)
            {
                try { clientUdpClient.Send(data.Array, data.Count, new IPEndPoint(address, serverBroadcastListenPort)); }
                catch (SocketException exception)
                {
                    if (LastError == null) Debug.LogWarning("[LAN] Probe failed: " + exception.SocketErrorCode);
                    LastError = exception.SocketErrorCode.ToString();
                }
            }
        }
    }

    protected override ServerResponse ProcessRequest(ServerRequest request, IPEndPoint endpoint)
    {
        Debug.Log("[LAN] Search request received from " + endpoint.Address);
        var response = base.ProcessRequest(request, endpoint);
        response.serverId = instanceId;
        return response;
    }

    protected override void ProcessResponse(ServerResponse response, IPEndPoint endpoint)
    {
        Debug.Log("[LAN] Host response received from " + endpoint.Address);
        base.ProcessResponse(response, endpoint);
    }
}
