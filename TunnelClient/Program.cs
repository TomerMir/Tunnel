using System;
using System.IO;
using System.Threading;
using TunnelUtils;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.Models;
using System.Net;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Text;
using Titanium.Web.Proxy.Exceptions;

namespace TunnelClient
{

    public class EndpointSetting
    {
        public string Host { get; set; }
        public int Port { get; set; }

        public DnsEndPoint DnsEndPoint { 
            get { return new DnsEndPoint(Host, Port); }
        }

        public override string ToString()
        {
            return $"Host: {Host}  Port: {Port}";
        }

        public bool IsValid()
        {
            return !string.IsNullOrWhiteSpace(Host) && Port > 0 && Port < ushort.MaxValue;
        }

        public string Authority
        {
            get { return $"{Host}:{Port}"; }
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            Configuration.Init(args);
            StartClient();
        }






        

        static void StartClient()
        {
            EndPoint proxyEndpoint = null;
            if (Configuration.InternalProxyEndpoint.Host == null)
            {
                Logger.Info("No external proxy was provided, using internal proxy");
                var proxyServer = new ProxyServer(false, false, false);
                var internalProxyEndpoint = new ExplicitProxyEndPoint(IPAddress.Loopback, 0, decryptSsl: false);

                //This is a dummy certificate to eliminate exceptions when trying to generate a certificate.
                //This proxy is not set to decrypt connections, therefore, there is no need in root certificate. 
                internalProxyEndpoint.GenericCertificate = new System.Security.Cryptography.X509Certificates.X509Certificate2();
                proxyServer.AddEndPoint(internalProxyEndpoint);
                proxyServer.ExceptionFunc += ProxyException;
                if (Configuration.UseSystemProxyConfig)
                {
                    proxyServer.ForwardToUpstreamGateway = true;
                    Logger.Info($"Using system proxy config for upstream connections");
                }


                try
                {
                    proxyServer.Start();
                }
                catch (Exception ex)
                {
                    Logger.Fatal(ex, "Failed to start embedded proxy");
                    Environment.Exit(-1);
                }
                proxyEndpoint = new IPEndPoint(internalProxyEndpoint.IpAddress, internalProxyEndpoint.Port);
                Logger.Info($"Internal Proxy Endpoint: {proxyEndpoint}");
            }
            else
            {
                proxyEndpoint = Configuration.InternalProxyEndpoint.DnsEndPoint;
                Logger.Info($"External Proxy Endpoint: {proxyEndpoint}");
            }

            //if (tunnelEndpoint == null)
            //{
            //    tunnelEndpoint = Consts.TUNNEL_ENDPOINT;
            //}

            if (!Configuration.TunnelServerEndpoint.IsValid())
            {
                Logger.Fatal("Invalid value for TunnelServerEndpoint was specified.");
                Environment.Exit(-1);
            }
            Logger.Info($"Tunnel Endpoint {Configuration.TunnelServerEndpoint}");

            Stream TunnelStream = null;

            try
            {
                const int nTrials = 3;
                int iTrial = 0;
                do
                {
                    iTrial++;
                    TunnelStream = TunnelConnection.ConnectToServer();
                    if (TunnelStream == null && iTrial < nTrials)
                    {
                        Logger.Info("Can't connect to server... retrying");
                    }
                }
                while (TunnelStream == null && iTrial < nTrials);

                if (TunnelStream == null)
                {
                    Logger.Info("Failed to connect to server - exiting");
                    return;
                }

                Thread PrintConnectionsCount = new Thread(() =>
                {
                    while (true)
                    {
                        Logger.Debug("Number of connections: " + ConnectionsDictionary.GetNumberOfConnections().ToString());
                        Thread.Sleep(7500);
                    }
                });

                PrintConnectionsCount.Start();

                while (true)
                {
                    byte[] buffer;
                    try
                    {
                        buffer = WebSocketUtils.ParseFrame(TunnelStream, mask: false);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Failed to read from the tunnel: " + ex.Message);
                        TunnelStream = TunnelConnection.ReconnectToServer();
                        if (TunnelStream == null)
                        {
                            Environment.Exit(-1);
                        }
                        continue;
                    }
                    Logger.Trace("Received message from tunnel, type: " + ((MessageType)buffer[0]).ToString());
                    Message message = Message.Recive(buffer);
                    if (message.Type == MessageType.TunnelClosed)
                    {
                        Logger.Error("Tunnel was closed by the server (Through Message)");
                        TunnelStream = TunnelConnection.ReconnectToServer();
                        if (TunnelStream == null)
                        {
                            Environment.Exit(-1);
                        }
                        continue;
                    }
                    Logger.Trace("Received message from tunnel, type: " + ((MessageType)buffer[0]).ToString() + ", Id : #" + message.ID);
                    switch (message.Type)
                    {
                        case MessageType.Open:
                            ConnectionHandler CH = new ConnectionHandler(proxyEndpoint, TunnelStream, message.ID);
                            break;

                        case MessageType.Close:
                            ConnectionsDictionary.Remove(message.ID);
                            break;

                        case MessageType.Message:
                            ConnectionsDictionary.SendMessage(message);
                            Logger.Trace("Sent message to #" + message.ID.ToString());
                            break;
                    }
                }
            }

            catch (Exception ex)
            {
                Logger.Info("Failure: " + ex.Message);
            }

            finally
            {
                if (TunnelStream != null)
                {
                    TunnelStream.Close();
                }
            }
        }

        static void ProxyException(Exception exception)
        {
            string logMessage = GetLogMessage(exception);
            Logger.Debug("Proxy exception: " + logMessage);
        }


        static string GetLogMessage(Exception exception)
        {
            StringBuilder message = new StringBuilder(exception.Message);
            Exception lastValidException = exception;
            Exception e = exception.InnerException;
            int depthLimit = 5;
            while (e != null && depthLimit > 0)
            {
                lastValidException = e;
                message.Append(" | InnerException: " + e.Message);
                e = e.InnerException;
                depthLimit--;
            }

            string logMessage;
            if (exception is ProxyHttpException pEx)
            {

                logMessage = $"Unhandled Proxy Exception. UserData = {pEx.Session?.UserData}, URL = {pEx.Session?.HttpClient.Request.RequestUri} Exception = {message}";
            }
            else
            {
                logMessage = $"Unhandled Exception: {lastValidException.GetType()} {message}";
            }
            return logMessage;
        }

    }
}
