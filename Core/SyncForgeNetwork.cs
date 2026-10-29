using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SyncForgeNetworkManager
{
    private TcpListener server;
    private TcpClient client;
    private NetworkStream stream;
    private Thread networkThread;

    public bool IsHosting { get; private set; }
    public bool IsClientConnected { get; private set; }
    public bool IsConnected => IsHosting || IsClientConnected;

    public Action<SyncForgePeerData> OnDataReceived;

    public void StartHost(int port)
    {
        if (IsClientConnected) return;
        IsHosting = true;
        server = new TcpListener(IPAddress.Any, port);
        server.Start();
        networkThread = new Thread(HostListenerLoop);
        networkThread.IsBackground = true;
        networkThread.Start();
        Debug.Log("[SyncForge] Servidor aberto na porta " + port);
    }

    public void ConnectClient(string ip, int port)
    {
        if (IsHosting)
        {
            Debug.LogWarning("[SyncForge] Você está hospedando uma sala. Não é possível se conectar na própria sessão!");
            return;
        }
        if (IsClientConnected) return;

        networkThread = new Thread(() => ClientConnectLoop(ip, port));
        networkThread.IsBackground = true;
        networkThread.Start();
    }

    private void HostListenerLoop()
    {
        try
        {
            TcpClient acceptedClient = server.AcceptTcpClient();
            client = acceptedClient;
            stream = client.GetStream();
            IsClientConnected = true; // Um cliente conectou ao host
            Debug.Log("[SyncForge] Cliente conectado com sucesso!");
            ReadLoop();
        }
        catch (Exception e) { Debug.Log("[SyncForge] Erro no Host: " + e.Message); }
    }

    private void ClientConnectLoop(string ip, int port)
    {
        try
        {
            client = new TcpClient();
            client.Connect(ip, port);
            stream = client.GetStream();
            IsClientConnected = true;
            Debug.Log("[SyncForge] Conectado ao Host!");
            ReadLoop();
        }
        catch (Exception e) { Debug.Log("[SyncForge] Erro ao conectar: " + e.Message); }
    }

    private void ReadLoop()
    {
        byte[] buffer = new byte[4096];
        while (IsConnected && stream != null)
        {
            try
            {
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0) break;

                string jsonStr = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                SyncForgeNetworkMessage msg = JsonUtility.FromJson<SyncForgeNetworkMessage>(jsonStr);

                if (msg != null && !string.IsNullOrEmpty(msg.payloadJson))
                {
                    SyncForgePeerData data = JsonUtility.FromJson<SyncForgePeerData>(msg.payloadJson);
                    OnDataReceived?.Invoke(data);
                }
            }
            catch { break; }
        }
        Disconnect();
    }

    public void SendData(string type, SyncForgePeerData peerData)
    {
        if (!IsConnected || stream == null) return;

        try
        {
            SyncForgeNetworkMessage msg = new SyncForgeNetworkMessage
            {
                messageType = type,
                payloadJson = JsonUtility.ToJson(peerData)
            };

            string jsonStr = JsonUtility.ToJson(msg);
            byte[] dataBytes = Encoding.UTF8.GetBytes(jsonStr);
            stream.Write(dataBytes, 0, dataBytes.Length);
            stream.Flush();
        }
        catch (Exception e) { Debug.Log("[SyncForge] Erro ao enviar dados: " + e.Message); }
    }

    public void Disconnect()
    {
        IsHosting = false;
        IsClientConnected = false;
        stream?.Close();
        client?.Close();
        server?.Stop();
    }
}SyncForgeNetwork.cs
