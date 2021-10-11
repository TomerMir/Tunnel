using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using TunnelUtils;

namespace TunnelServer
{
    public class ServerLitseners
    {
        private Socket _browserListeningSocket;
        private Socket _tunnelListeningSocket;
        private Thread _waitForConnections;
        private Stream _tunnelStream;
        private string _secretKeyHash = null;
        private SslServerAuthenticationOptions _serverSslOptions = null;
        private object _lock = new object();
        private ManualResetEvent _tunnelEstablishedEvent = new ManualResetEvent(false);

        public SslServerAuthenticationOptions ServerSslOptions
        {
            get
            {
                if (_serverSslOptions == null)
                {
                    _serverSslOptions = new SslServerAuthenticationOptions
                    {
                        EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                        RemoteCertificateValidationCallback = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
                        {
                            Logger.Debug($"RemoteCertificateValidationCallback: {certificate}");
                            return true;
                        },
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                        ClientCertificateRequired = true,
                        ServerCertificate = CertUtils.BuildSelfSignedServerCertificate("TunnelServer")

                    };
                }
                return _serverSslOptions;
            }
        }

        public ServerLitseners(int tunnelPort, int proxyPort, bool requireClientCert, string secretKeyHash = null)
        {
            ServerSslOptions.ClientCertificateRequired = requireClientCert;
            Logger.Info($"Require TLS client certificate: {requireClientCert}");
            _secretKeyHash = secretKeyHash;
            _tunnelListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            Logger.Debug("Tunnel Socket() is OK");
            IPEndPoint TunnelListeningEndPoint = new IPEndPoint(IPAddress.Any, tunnelPort);
            Logger.Debug("Tunnel IPEndPoint() is OK");
            _tunnelListeningSocket.Bind(TunnelListeningEndPoint);
            _tunnelListeningSocket.Listen(5);

            _browserListeningSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            IPEndPoint BrowserListeningEndPoint = new IPEndPoint(IPAddress.Loopback, proxyPort);
            _browserListeningSocket.Bind(BrowserListeningEndPoint);
            Logger.Info($"Proxy listen endpoint: {_browserListeningSocket.LocalEndPoint}");
        }

        public void LitsenForConnections()
        {
            _waitForConnections = new Thread(() =>
            {
                Socket browserConnection;
                _browserListeningSocket.Listen(5);
                Logger.Debug("Awaiting Browser connections...");
                while (true)
                {
                    browserConnection = _browserListeningSocket.Accept();
                    if (_tunnelStream == null)
                    {
                        browserConnection.Close();
                        continue;
                    }
                    ConnectionHandler ch = new ConnectionHandler(browserConnection, _tunnelStream);
                }
            });
            _waitForConnections.Start();
            Logger.Debug("Started WaitForConnections thread");
        }

        public void HandleAcceptedTunnelSocket(Socket tunnelSocket, int iThread)
        {
            try
            {
                Logger.Info($"New connection was accepted from {(IPEndPoint)tunnelSocket.RemoteEndPoint}  (thread {iThread})");
                if (_tunnelStream != null)
                {
                    Logger.Info($"New connection was accepted, but the tunnel is already active - closing it (thread {iThread})");
                    tunnelSocket.Close();
                    return;
                }
                else
                {
                    Stream newTunnelStream = TunnelHandshake(tunnelSocket, iThread);
                    if (newTunnelStream == null)
                    {
                        tunnelSocket.Close();
                        return;
                    }
                    else if (_tunnelStream == null)
                    {
                        lock (_lock)
                        {
                            if (_tunnelStream == null)
                            {
                                Logger.Info($"New connection is set for the tunnel (thread {iThread})");
                                _tunnelStream = newTunnelStream;
                                _tunnelEstablishedEvent.Set();
                            }
                        }
                    }

                    if (_tunnelStream != newTunnelStream)
                    {
                        //The new tunnel stream is not the chosen one - close it
                        Logger.Info($"New connection was handled, but the tunnel is already active - closing it (thread {iThread})");
                        newTunnelStream.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error when handling new connection");
            }

        }


        public Stream LitsenForTunnelClient()
        {
            new Thread(() =>
            {
                int iThread = 0;
                while (true)
                {
                    Logger.Info($"Awaiting tunnel connections (trial {iThread})");
                    Socket tunnelSocket = _tunnelListeningSocket.Accept();
                    new Thread(() => HandleAcceptedTunnelSocket(tunnelSocket, iThread)).Start();
                    iThread++;
                }
            }).Start();

            _tunnelEstablishedEvent.WaitOne();
            _tunnelEstablishedEvent.Reset();
            return _tunnelStream;
        }



        public Stream TunnelHandshake(Socket tunnelSocket, int iThread)
        {
            try
            {
                Logger.Info($"Handling new tunnel socket:{(IPEndPoint)tunnelSocket.RemoteEndPoint}  (thread: {iThread})");
                var netStream = new NetworkStream(tunnelSocket, true);
                netStream.ReadTimeout = 15000;
                netStream.WriteTimeout = 15000;
                if (_tunnelStream != null)
                {
                    Logger.Debug($"TunnelStream is already set - stopping handshake (thread: {iThread})");
                    return null;
                }
                var sslStream = new SslStream(netStream, false);
                Logger.Debug($"Authenticating TLS connection (thread: {iThread})");
                if (ServerSslOptions.ServerCertificate != null)
                {
                    Logger.Debug($"Using server certificate: {ServerSslOptions.ServerCertificate}  (thread: {iThread})");
                }

                sslStream.AuthenticateAsServer(ServerSslOptions);
                Logger.Info($"Using secured socket: IsAuthenticated: {sslStream.IsAuthenticated} IsEncrypted: {sslStream.IsEncrypted} IsMutuallyAuthenticated: {sslStream.IsMutuallyAuthenticated}  IsSigned: {sslStream.IsSigned} CanRead: {sslStream.CanRead} CanWrite:{sslStream.CanWrite}  (thread: {iThread})");

                if (_tunnelStream != null)
                {
                    Logger.Debug($"TunnelStream is already set - stopping handshake (thread: {iThread})");
                    return null;
                }

                bool status = WebSocketUtils.ServerHandshake(sslStream, _secretKeyHash);
                sslStream.ReadTimeout = -1;
                sslStream.WriteTimeout = -1;

                if (!status)
                {
                    Logger.Error($"Failed to perform WebSocket handshake + Secret Key validation  (thread: {iThread})");
                    sslStream.Close();
                    sslStream = null;
                }
                else
                {
                    Logger.Info($"WebSocket handshake was completed and client secret key was successfully validated  (thread: {iThread})");
                }
                return sslStream;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to listen to TunnelClient  (thread: {iThread})");
                return null;
            }
        }





        public Stream NewTunnelConnection()
        {
            Logger.Info("Tunnel disconnected, waiting for new connection");
            lock (_lock)
            {
                _tunnelStream = null;
                ConnectionsDictionary.RemoveAllConnections();
            }
            _tunnelEstablishedEvent.WaitOne();
            _tunnelEstablishedEvent.Reset();
            return _tunnelStream;
        }

        public void CloseAll()
        {
            _browserListeningSocket.Close();
            _tunnelListeningSocket.Close();
        }
    }
}
