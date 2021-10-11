using System;
using System.Net.Sockets;
using TunnelUtils;
using System.Threading;
using System.Net.Security;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Net;
using System.Text;

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
                        TargetHost = Configuration.TunnelServerEndpoint.Host,
                        RemoteCertificateValidationCallback = CertValidator,
                        ClientCertificates = new X509CertificateCollection(new[] { CertUtils.BuildSelfSignedServerCertificate("cloud.appsechcl.com") as X509Certificate })
                    };
                }
                return _clientSslOptions;
            }
        }
        static RemoteCertificateValidationCallback CertValidator = (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors) =>
        {
            //TODO: Add certificate validation (should get a valid certificate from the server that matches the tunnel server hostname
            Logger.Debug("TLS Handshake - got the following server certificate: " + certificate.ToString());
            return true;
        };


        static byte[] GetConnectRequest()
        {
            StringBuilder sb = new StringBuilder(1024);
            string authority = Configuration.TunnelServerEndpoint.Authority;
            sb.Append($"CONNECT {authority} HTTP/1.1\r\nHost: {authority}\r\n");
            if (!string.IsNullOrEmpty(Configuration.OutgoingProxyCredentials))
            {
                string basicAuthorizationPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(Configuration.OutgoingProxyCredentials));
                sb.Append($"Proxy-Authorization: basic {basicAuthorizationPayload}\r\n");
            }
            sb.Append("\r\n");
            return Encoding.UTF8.GetBytes(sb.ToString());
        }

        public static Stream ConnectToServer()
        {
            try
            {
                Socket tunnelSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
                Logger.Debug("Initialized socket");
                NetworkStream tunnelStream;
                //If tunnel should path through proxy, here is the place to establish the connection
                if (Configuration.OutgoingProxyEndpoint.IsValid())
                {
                    Logger.Info($"Connecting to Tunnel Server through proxy: {Configuration.OutgoingProxyEndpoint}");
                    tunnelSocket.Connect(Configuration.OutgoingProxyEndpoint.DnsEndPoint);
                    tunnelStream = new NetworkStream(tunnelSocket, true);
                    byte[] connectReq = GetConnectRequest();
                    tunnelStream.Write(connectReq, 0, connectReq.Length);
                    byte[] respBuff = new byte[4096];
                    int nBytes = WebSocketUtils.ReadHeadersSection(tunnelStream, respBuff);
                    if (nBytes == WebSocketUtils.HEADERS_TOO_LARGE)
                    {
                        WebSocketUtils.LogBuffer(respBuff);
                        throw new Exception("Response to CONNECT request for outgoing proxy was too large.");
                    }
                    if (nBytes == WebSocketUtils.CONNECTION_CLOSED)
                    {
                        WebSocketUtils.LogBuffer(respBuff);
                        throw new Exception("Connection was closed in the middle of Response to CONNECT request for outgoing proxy.");
                    }
                    else
                    {
                        const byte LF = 0x0A;
                        int iEndFirstLine = Array.IndexOf(respBuff, LF, 0, nBytes);
                        string firstLine = Encoding.UTF8.GetString(respBuff, 0, iEndFirstLine);
                        if (!firstLine.Contains(" 200 "))
                        {
                            WebSocketUtils.LogBuffer(respBuff, 0, nBytes);
                            throw new Exception($"Error response to CONNECT request for outgoing proxy: {firstLine}");
                        }
                    }

                }
                else
                {
                    tunnelSocket.Connect(Configuration.TunnelServerEndpoint.DnsEndPoint);
                    tunnelStream = new NetworkStream(tunnelSocket, true);
                }
                Logger.Info("Connected to tunnel server");



                
                SslStream sslStream = new SslStream(tunnelStream, false);

                //If there is no connection to the tunnel server, sslStream.AuthenticateAsClient hangs.
                //We temporarily set timeout for the SSL handshake and WebSocket handshake, and then set it back to the original
                //timeout (which is infinite by default)
                int origReadTimeout = sslStream.ReadTimeout;
                int origWriteTimeout = sslStream.WriteTimeout;
                sslStream.ReadTimeout = 15000;
                sslStream.WriteTimeout = 15000;
                Logger.Debug("Authenticating TLS connection");
                if (ClientSslOptions.ClientCertificates.Count > 0)
                {
                    Logger.Debug("Using client certificate: " + ClientSslOptions.ClientCertificates[0]);
                }
                sslStream.AuthenticateAsClient(ClientSslOptions);
                Logger.Info($"Got new stream from socket IsAuthenticated: {sslStream.IsAuthenticated} IsEncrypted: {sslStream.IsEncrypted} IsMutuallyAuthenticated: {sslStream.IsMutuallyAuthenticated} IsSigned: {sslStream.IsSigned} CanRead: {sslStream.CanRead} CanWrite:{sslStream.CanWrite}");

                bool rc = WebSocketUtils.ClientHandshake(sslStream, Configuration.TunnelServerEndpoint.Host, Configuration.Key);
                if (!rc)
                {
                    Logger.Error("Can't connect to server - WebSocket handshake failed");
                    return null;
                }
                sslStream.ReadTimeout = origReadTimeout;
                sslStream.WriteTimeout = origWriteTimeout;

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
