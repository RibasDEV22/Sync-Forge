
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;

public sealed class SyncForgeNetworkManager : IDisposable
{
    private const int ProtocolVersion = 2;
    private const int ConnectTimeoutMs = 5000;
    private const int MaxMessageLength = 65536;

    private readonly object stateLock = new object();
    private readonly object sendLock = new object();

    private readonly ConcurrentQueue<string> incomingMessages =
        new ConcurrentQueue<string>();

    private readonly ConcurrentQueue<string> pendingLogs =
        new ConcurrentQueue<string>();

    private TcpListener server;
    private TcpClient client;
    private NetworkStream stream;
    private StreamReader reader;
    private StreamWriter writer;
    private Thread networkThread;

    private volatile bool isRunning;
    private bool isHosting;
    private bool isPeerConnected;
    private bool disposed;

    public event Action<SyncForgePeerData> OnDataReceived;

    public bool IsHosting
    {
        get
        {
            lock (stateLock)
                return isHosting;
        }
    }

    public bool IsClientConnected
    {
        get
        {
            lock (stateLock)
                return isPeerConnected;
        }
    }

    // Só considera conectado quando existe um peer efetivamente conectado.
    public bool IsConnected => IsClientConnected;

    public void StartHost(int port)
    {
        ValidatePort(port);
        Disconnect();

        TcpListener listener = null;

        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();

            lock (stateLock)
            {
                ThrowIfDisposed();

                server = listener;
                isHosting = true;
                isRunning = true;

                networkThread = new Thread(HostListenerLoop)
                {
                    IsBackground = true,
                    Name = "SyncForge Host Listener"
                };

                networkThread.Start();
            }

            Log("Servidor iniciado na porta " + port);
        }
        catch (Exception exception)
        {
            try { listener?.Stop(); } catch { }

            Disconnect();

            Log("Erro ao iniciar servidor: " + exception.Message);
        }
    }

    public void ConnectClient(string ip, int port)
    {
        ValidatePort(port);

        if (string.IsNullOrWhiteSpace(ip))
            throw new ArgumentException("O endereço IP não pode estar vazio.");

        if (IsHosting || IsClientConnected)
            return;

        Disconnect();

        lock (stateLock)
        {
            ThrowIfDisposed();

            isRunning = true;

            networkThread = new Thread(() => ClientConnectLoop(ip.Trim(), port))
            {
                IsBackground = true,
                Name = "SyncForge Client"
            };

            networkThread.Start();
        }
    }

    private void HostListenerLoop()
    {
        while (isRunning)
        {
            TcpClient acceptedClient = null;

            try
            {
                TcpListener currentServer;

                lock (stateLock)
                    currentServer = server;

                if (currentServer == null)
                    break;

                acceptedClient = currentServer.AcceptTcpClient();

                if (!isRunning)
                {
                    acceptedClient.Close();
                    break;
                }

                acceptedClient.NoDelay = true;

                if (!AttachClient(acceptedClient))
                {
                    acceptedClient.Close();
                    continue;
                }

                Log("Cliente conectado ao servidor.");

                ReadLoop();

                ClosePeer();
            }
            catch (SocketException exception)
            {
                if (isRunning)
                    Log("Erro no listener: " + exception.Message);

                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception exception)
            {
                if (isRunning)
                    Log("Erro no servidor: " + exception.Message);

                ClosePeer();
            }
        }
    }

    private void ClientConnectLoop(string ip, int port)
    {
        TcpClient newClient = null;

        try
        {
            IPAddress parsedAddress;

            if (!IPAddress.TryParse(ip, out parsedAddress))
                throw new ArgumentException("Endereço IP inválido.");

            newClient = new TcpClient();
            newClient.NoDelay = true;

            IAsyncResult result = newClient.BeginConnect(
                parsedAddress,
                port,
                null,
                null
            );

            bool connected = result.AsyncWaitHandle.WaitOne(
                ConnectTimeoutMs
            );

            // Libera o handle utilizado pela operação assíncrona.
            using (result.AsyncWaitHandle)
            {
                if (!connected)
                    throw new TimeoutException("Tempo limite de conexão excedido.");

                newClient.EndConnect(result);
            }

            if (!isRunning)
            {
                newClient.Close();
                return;
            }

            if (!AttachClient(newClient))
            {
                newClient.Close();
                return;
            }

            Log("Conectado ao Host.");

            ReadLoop();
        }
        catch (Exception exception)
        {
            if (isRunning)
                Log("Falha na conexão: " + exception.Message);
        }
        finally
        {
            ClosePeer();

            if (!IsHosting)
                StopClientSession();
        }
    }

    private bool AttachClient(TcpClient newClient)
    {
        try
        {
            NetworkStream newStream = newClient.GetStream();

            StreamReader newReader = new StreamReader(
                newStream,
                System.Text.Encoding.UTF8,
                false,
                4096,
                true
            );

            StreamWriter newWriter = new StreamWriter(
                newStream,
                new System.Text.UTF8Encoding(false),
                4096,
                true
            )
            {
                AutoFlush = true
            };

            lock (stateLock)
            {
                if (!isRunning || disposed || isPeerConnected)
                {
                    newReader.Dispose();
                    newWriter.Dispose();
                    return false;
                }

                client = newClient;
                stream = newStream;
                reader = newReader;
                writer = newWriter;
                isPeerConnected = true;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void ReadLoop()
    {
        StreamReader currentReader;

        lock (stateLock)
            currentReader = reader;

        if (currentReader == null)
            return;

        try
        {
            while (isRunning && !currentReader.EndOfStream)
            {
                string line = ReadBoundedLine(currentReader);

                if (line == null)
                    break;

                if (string.IsNullOrWhiteSpace(line))
                    continue;

                incomingMessages.Enqueue(line);
            }
        }
        catch (Exception exception)
        {
            if (isRunning)
                Log("Erro durante recebimento: " + exception.Message);
        }
    }

    // Impede que uma única linha de protocolo consuma memória ilimitada.
    private static string ReadBoundedLine(StreamReader currentReader)
    {
        System.Text.StringBuilder builder =
            new System.Text.StringBuilder(256);

        while (true)
        {
            int value = currentReader.Read();

            if (value == -1)
            {
                if (builder.Length == 0)
                    return null;

                break;
            }

            if (value == '\n')
                break;

            if (value == '\r')
                continue;

            if (builder.Length >= MaxMessageLength)
                throw new InvalidDataException("Mensagem excedeu o limite permitido.");

            builder.Append((char)value);
        }

        return builder.ToString();
    }

    // Executado exclusivamente na thread principal do Unity.
    public void PumpIncomingMessages()
    {
        while (pendingLogs.TryDequeue(out string log))
            Debug.Log("[SyncForge] " + log);

        while (incomingMessages.TryDequeue(out string json))
        {
            try
            {
                SyncForgeNetworkMessage message =
                    JsonUtility.FromJson<SyncForgeNetworkMessage>(json);

                if (message == null ||
                    message.messageType != "CAM_SYNC" ||
                    string.IsNullOrEmpty(message.payloadJson))
                    continue;

                SyncForgePeerData data =
                    JsonUtility.FromJson<SyncForgePeerData>(
                        message.payloadJson
                    );

                if (data == null ||
                    !data.IsValid() ||
                    !data.HasValidTransform())
                    continue;

                OnDataReceived?.Invoke(data);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "[SyncForge] Mensagem inválida: " + exception.Message
                );
            }
        }
    }

    public void SendData(string type, SyncForgePeerData peerData)
    {
        if (string.IsNullOrWhiteSpace(type) || peerData == null)
            return;

        if (!IsConnected)
            return;

        try
        {
            SyncForgeNetworkMessage message = new SyncForgeNetworkMessage
            {
                messageType = type,
                payloadJson = JsonUtility.ToJson(peerData)
            };

            string json = JsonUtility.ToJson(message);

            if (json.Length > MaxMessageLength)
                throw new InvalidDataException("Mensagem de envio muito grande.");

            lock (sendLock)
            {
                StreamWriter currentWriter;

                lock (stateLock)
                {
                    if (!isPeerConnected)
                        return;

                    currentWriter = writer;
                }

                if (currentWriter == null)
                    return;

                currentWriter.WriteLine(json);
            }
        }
        catch (Exception exception)
        {
            Log("Falha no envio: " + exception.Message);
            Disconnect();
        }
    }

    private void ClosePeer()
    {
        TcpClient oldClient;
        StreamReader oldReader;
        StreamWriter oldWriter;

        lock (stateLock)
        {
            oldClient = client;
            oldReader = reader;
            oldWriter = writer;

            client = null;
            stream = null;
            reader = null;
            writer = null;

            isPeerConnected = false;
        }

        try { oldReader?.Dispose(); } catch { }
        try { oldWriter?.Dispose(); } catch { }
        try { oldClient?.Close(); } catch { }
    }

    private void StopClientSession()
    {
        lock (stateLock)
        {
            isRunning = false;
            isPeerConnected = false;
        }
    }

    public void Disconnect()
    {
        TcpListener oldServer;
        Thread oldThread;

        lock (stateLock)
        {
            isRunning = false;
            isHosting = false;
            isPeerConnected = false;

            oldServer = server;
            server = null;

            oldThread = networkThread;
            networkThread = null;
        }

        ClosePeer();

        try { oldServer?.Stop(); } catch { }

        if (oldThread != null &&
            oldThread != Thread.CurrentThread &&
            oldThread.IsAlive)
        {
            oldThread.Join(500);
        }

        while (incomingMessages.TryDequeue(out _)) { }
    }

    private void Log(string message)
    {
        pendingLogs.Enqueue(message);
    }

    private static void ValidatePort(int port)
    {
        if (port < 1 || port > 65535)
            throw new ArgumentOutOfRangeException(
                nameof(port),
                "A porta deve estar entre 1 e 65535."
            );
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(SyncForgeNetworkManager));
    }

    public void Dispose()
    {
        if (disposed)
            return;

        Disconnect();
        disposed = true;
    }
}
