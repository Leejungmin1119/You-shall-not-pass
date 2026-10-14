using System;
using System.Collections.Generic;
using System.Text;
using Mirror;
using Mirror.SimpleWeb;
using UnityEngine;

// Host와 참가자 모두 공개 중계 서버에 바깥 방향으로 WebSocket을 연결한다.
// 비밀번호는 URL이나 로그에 넣지 않고 TLS 연결 후 첫 메시지로 전달한다.
public class PasswordRelayTransport : Transport
{
    [SerializeField] private string relayUrl = "";
    public string RoomPassword { private get; set; } = "";
    public bool RoomOpen { get; private set; }
    public event Action<string> Failed;
    private SimpleWebClient hostSocket, clientSocket;
    private bool listening, connected, clientAttempt;
    private float hostStarted, clientStarted, hostReceived, clientReceived, nextHostPing, nextClientPing;
    private readonly HashSet<int> peers = new HashSet<int>();
    private const int MaxPayload = 65536;

    [Serializable] private struct Hello { public int version; public string mode; public string password; }
    public bool ValidateSettings(string password, out string reason)
    {
        if (!Uri.TryCreate(relayUrl, UriKind.Absolute, out Uri url))
        { reason = "Online rooms are not available yet."; return false; }
        if (url.Scheme != "wss" && !(url.Scheme == "ws" && url.IsLoopback))
        { reason = "The room service needs a secure connection."; return false; }
        if (url.AbsolutePath != "/relay" || !string.IsNullOrEmpty(url.Query) || !string.IsNullOrEmpty(url.UserInfo))
        { reason = "The room service is not configured correctly."; return false; }
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8 || password.Length > 64)
        { reason = "Use a password with 8-64 characters."; return false; }
        reason = null; return true;
    }
    private SimpleWebClient Open(bool host)
    {
        var socket = SimpleWebClient.Create(MaxPayload + 5, 1000, new TcpConfig(true, 5000, 60000));
        socket.onConnect += () => {
            if (host ? hostSocket != socket : clientSocket != socket) return;
            var hello = new Hello { version = 1, mode = host ? "host" : "join", password = RoomPassword };
            byte[] json = Encoding.UTF8.GetBytes(JsonUtility.ToJson(hello));
            byte[] packet = new byte[json.Length + 1]; packet[0] = 1;
            Buffer.BlockCopy(json, 0, packet, 1, json.Length); socket.Send(new ArraySegment<byte>(packet));
        };
        socket.onData += data => {
            if (host ? hostSocket != socket : clientSocket != socket) return;
            if (host) hostReceived = Time.unscaledTime; else clientReceived = Time.unscaledTime;
            Handle(host, data);
        };
        socket.onError += _ => {
            if (host ? hostSocket == socket : clientSocket == socket) Fail(host, "Could not reach the room service.");
        };
        socket.onDisconnect += () => {
            if (host ? hostSocket == socket : clientSocket == socket) Fail(host, "Connection to the room was lost.");
        };
        return socket;
    }
    private void Fail(bool host, string reason)
    {
        Failed?.Invoke(reason);
        if (host) { RoomOpen = false; OnServerError?.Invoke(0, TransportError.ConnectionClosed, reason); ServerStop(); }
        else { OnClientError?.Invoke(TransportError.ConnectionClosed, reason); ClientDisconnect(); }
    }
    private static int PeerId(ArraySegment<byte> data)
    {
        int i = data.Offset + 1;
        return data.Array[i] | data.Array[i+1] << 8 | data.Array[i+2] << 16 | data.Array[i+3] << 24;
    }
    private void Handle(bool host, ArraySegment<byte> packet)
    {
        if (packet.Count == 0) { Fail(host, "Invalid room response."); return; }
        byte type = packet.Array[packet.Offset];
        if (type == 9) { Fail(host, Encoding.UTF8.GetString(packet.Array, packet.Offset + 1, packet.Count - 1)); return; }
        if (type == 8 && packet.Count == 1) return;
        if (type == 2 && packet.Count == 1)
        {
            if (host) RoomOpen = true;
            else if (!connected) { connected = true; OnClientConnected?.Invoke(); }
            return;
        }
        if (packet.Count < 5) { Fail(host, "Invalid room response."); return; }
        int peer = PeerId(packet);
        if (host && type == 3 && peer > 0 && peers.Add(peer)) OnServerConnectedWithAddress?.Invoke(peer, "relay-peer-" + peer);
        else if (host && type == 5 && peers.Remove(peer)) OnServerDisconnected?.Invoke(peer);
        else if (type == 4 && packet.Count > 5)
        {
            var payload = new ArraySegment<byte>(packet.Array, packet.Offset + 5, packet.Count - 5);
            if (host && peers.Contains(peer)) OnServerDataReceived?.Invoke(peer, payload, Channels.Reliable);
            else if (!host && connected) OnClientDataReceived?.Invoke(payload, Channels.Reliable);
        }
    }
    private static byte[] Frame(byte type, int peer, ArraySegment<byte> data)
    {
        byte[] packet = new byte[data.Count + 5]; packet[0] = type;
        for (int i = 0; i < 4; i++) packet[i + 1] = (byte)(peer >> (8 * i));
        if (data.Count > 0) Buffer.BlockCopy(data.Array, data.Offset, packet, 5, data.Count);
        return packet;
    }
    private void Poll(bool host)
    {
        var socket = host ? hostSocket : clientSocket;
        if (socket == null) return;
        socket.ProcessMessageQueue();
        if (host ? hostSocket != socket : clientSocket != socket) return;
        float now = Time.unscaledTime;
        bool ready = host ? RoomOpen : connected;
        if (!ready && now - (host ? hostStarted : clientStarted) > 15f)
        { Fail(host, "Room connection timed out."); return; }
        if (ready && now - (host ? hostReceived : clientReceived) > 25f)
        { Fail(host, "Room connection timed out."); return; }
        if (ready && now >= (host ? nextHostPing : nextClientPing))
        {
            socket.Send(new ArraySegment<byte>(new byte[] { 8 }));
            if (host) nextHostPing = now + 5; else nextClientPing = now + 5;
        }
    }
    public void LockRoom() { if (RoomOpen) hostSocket.Send(new ArraySegment<byte>(new byte[] { 7 })); }
    public override bool Available() => true;
    public override int GetMaxPacketSize(int channelId = Channels.Reliable) => MaxPayload;
    public override int GetBatchThreshold(int channelId = Channels.Reliable) => 16384;
    public override bool ServerActive() => listening;
    public override bool ClientConnected() => connected;
    public override Uri ServerUri() => Uri.TryCreate(relayUrl, UriKind.Absolute, out var uri) ? uri : new Uri("ws://localhost/relay");
    public override string ServerGetClientAddress(int connectionId) => "relay-peer-" + connectionId;
    public override void ServerStart()
    {
        if (!ValidateSettings(RoomPassword, out string reason)) { Failed?.Invoke(reason); return; }
        listening = true; RoomOpen = false; hostStarted = hostReceived = Time.unscaledTime;
        hostSocket = Open(true); hostSocket.Connect(new Uri(relayUrl));
    }
    public override void ClientConnect(string address)
    {
        clientAttempt = true;
        if (!ValidateSettings(RoomPassword, out string reason)) { Fail(false, reason); return; }
        connected = false; clientStarted = clientReceived = Time.unscaledTime;
        clientSocket = Open(false); clientSocket.Connect(new Uri(relayUrl));
    }
    public override void ClientSend(ArraySegment<byte> data, int channelId = Channels.Reliable)
    {
        if (!connected || data.Count > MaxPayload) return;
        clientSocket.Send(new ArraySegment<byte>(Frame(4, 0, data)));
        OnClientDataSent?.Invoke(data, Channels.Reliable);
    }
    public override void ServerSend(int connectionId, ArraySegment<byte> data, int channelId = Channels.Reliable)
    {
        if (!RoomOpen || !peers.Contains(connectionId) || data.Count > MaxPayload) return;
        hostSocket.Send(new ArraySegment<byte>(Frame(4, connectionId, data)));
        OnServerDataSent?.Invoke(connectionId, data, Channels.Reliable);
    }
    public override void ServerDisconnect(int connectionId)
    {
        if (!peers.Remove(connectionId)) return;
        hostSocket?.Send(new ArraySegment<byte>(Frame(6, connectionId, default)));
        OnServerDisconnected?.Invoke(connectionId);
    }
    public override void ClientDisconnect()
    {
        if (clientSocket == null && !clientAttempt) return;
        clientAttempt = false;
        var socket = clientSocket; clientSocket = null; connected = false;
        socket?.Disconnect(); OnClientDisconnected?.Invoke();
    }
    public override void ServerStop()
    {
        var socket = hostSocket; hostSocket = null; listening = RoomOpen = false;
        foreach (int peer in new List<int>(peers)) { peers.Remove(peer); OnServerDisconnected?.Invoke(peer); }
        socket?.Disconnect();
    }
    public override void ClientEarlyUpdate() => Poll(false);
    public override void ServerEarlyUpdate() => Poll(true);
    public override void Shutdown() { ClientDisconnect(); ServerStop(); }
}
