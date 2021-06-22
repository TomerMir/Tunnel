using System;
using System.Net.Sockets;
using TunnelUtils;
using System.Threading;
using System.Net.Security;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Net;

namespace TunnelClient
{
    public class TunnelConnection
    {

        private static SslClientAuthenticationOptions _clientSslOptions = null;
        static SslClientAuthenticationOptions ClientSslOptions
        {
            get
            {
                if (_clientSslOptions == null)
                {
                    _clientSslOptions = new SslClientAuthenticationOptions
                    {
                        EnabledSslProtocols = System.Security.Authentication.SslProtocols.Tls12,
                        AllowRenegotiation = true,
                        EncryptionPolicy = EncryptionPolicy.RequireEncryption,
                        TargetHost = "TunnelServer",
                        RemoteCertificateValidationCallback = CertValidator,
                        ClientCertificates = new X509CertificateCollection(new[] { CertUtils.BuildSelfSignedServerCertificate("TunnelClient") as X509Certificate })
                    };
                }
                return _clientSslOptions;
            }
        }
        static RemoteCertificateValidationCallback CertValidator = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
        {
            return true;
        };

        private static IPEndPoint _tunnelEndpoint;
        private static string _secretKey;

        public static Stream ConnectToServer(IPEndPoint tunnelEndpoint, string secretKey)
        {
            _tunnelEndpoint = tunnelEndpoint;
            _secretKey = secretKey;
            return ConnectToServer();
        }

        private static Stream ConnectToServer()
        {
            try
            {
                Socket tunnelSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
                Logger.Debug("Initialized socket");
                tunnelSocket.Connect(_tunnelEndpoint);
                Logger.Info("Connected to tunnel server");

                NetworkStream tunnelStream = new NetworkStream(tunnelSocket, true);
                SslStream sslStream = new SslStream(tunnelStream, false);
                sslStream.AuthenticateAsClient(ClientSslOptions);
                Logger.Info($"Got new stream from socket IsAuthenticated: {sslStream.IsAuthenticated} IsEncrypted: {sslStream.IsEncrypted} IsMutuallyAuthenticated: {sslStream.IsMutuallyAuthenticated}");

                bool rc = WebSocketUtils.ClientHandshake(sslStream, _tunnelEndpoint.Address.ToString(), _secretKey);
                if (!rc)
                {
                    Logger.Error("Can't connect to server - WebSocket handshake failed");
                    return null;
                }

                return sslStream;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Can't connect to server...");
                return null;
            }
        }

        public static Stream ReconnectToServer()
        {
            ConnectionsDictionary.RemoveAllConnections();
            for (int i = 1; i < 10; i++)
            {
                Logger.Info("Reconnecting to the server for the " + i.ToString() + " time");
                Stream tunnelStream = ConnectToServer();
                if (tunnelStream != null)
                {
                    return tunnelStream;
                }
                Thread.Sleep(2000);
            }
            Logger.Fatal("Failed to reconnect to Tunnel Server");
            return null;
        }
    }
}
