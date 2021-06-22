using System;
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

        public ServerLitseners(int tunnelPort, int proxyPort, string secretKeyHash = null)
        {
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

        public Stream LitsenForTunnelClient()
        {
            Logger.Info("Awaiting tunnel connections");
            Socket tunnelSocket = _tunnelListeningSocket.Accept();
            Logger.Info("Tunnel client connected");
            var netStream = new NetworkStream(tunnelSocket, true);

            var sslStream = new SslStream(netStream, false);
            sslStream.AuthenticateAsServer(ServerSslOptions);
            Logger.Info($"Using secured socket: IsAuthenticated: {sslStream.IsAuthenticated} IsEncrypted: {sslStream.IsEncrypted} IsMutuallyAuthenticated: {sslStream.IsMutuallyAuthenticated}");
            _tunnelStream = sslStream;

            bool status = WebSocketUtils.ServerHandshake(_tunnelStream, _secretKeyHash);
            if (!status)
            {
                Logger.Error("Failed to perform WebSocket handshake + Secret Key validation");
                _tunnelStream.Close();
                _tunnelStream = null;
            }
            else
            {
                Logger.Info("WebSocket handshake was completed and client secret key was successfully validated");
            }

            return _tunnelStream;
        }


        public Stream NewTunnelConnection()
        {
            Logger.Info("Tunnel disconnected, waiting for new connection");
            _tunnelStream = null;
            ConnectionsDictionary.RemoveAllConnections();
            Stream tunnelStream = LitsenForTunnelClient();
            return tunnelStream;
        }

        public void CloseAll()
        {
            _browserListeningSocket.Close();
            _tunnelListeningSocket.Close();
        }
    }
}
