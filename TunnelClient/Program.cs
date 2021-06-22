using System;
using System.IO;
using System.Threading;
using TunnelUtils;
using System.CommandLine;
using System.CommandLine.Invocation;
using Titanium.Web.Proxy;
using Titanium.Web.Proxy.Models;
using System.Net;
using System.CommandLine.Parsing;

namespace TunnelClient
{

    class Program
    {
        static void Main(string[] args)
        {

            // Create a root command with some options
            var rootCommand = new RootCommand
            {
                new Option<string>(new string[] {"--tunnel-endpoint","--tep" })
                {
                    Description = "IP Address and port of the tunnel server (<ip>:<port>)",
                    IsRequired = true,
                },
                new Option<string>(new string[] {"--external-proxy-endpoint","--epe" }, "External proxy endpoint (<ip>:<port>)"),

                new Option<string>(new string[] {"--key","--k" }, getDefaultValue: () => Consts.TEST_SECRET_KEY)
                {
                    Description = "Client's key (used for authenticating the client)",
                }


                //new Option<FileInfo>(
                //    new string[] {"--client-cert","--cc" },
                //    "Certificate file"),
            };

            rootCommand.Description = "Tunnel Client";
            //Note that the parameters of the handler method are matched according to the names of the options
            rootCommand.Handler = CommandHandler.Create<string, string, string>(StartClient);

            rootCommand.Invoke(args);

        }


        static void StartClient(string externalProxyEndpoint, string tunnelEndpoint, string key)
        {

            IPEndPoint proxyEndpoint = null;
            if (externalProxyEndpoint == null)
            {
                Logger.Info("No external proxy was provided, using internal proxy");
                var proxyServer = new ProxyServer(false, false, false);
                var internalProxyEndpoint = new ExplicitProxyEndPoint(IPAddress.Loopback, 0, decryptSsl: false);
                proxyServer.AddEndPoint(internalProxyEndpoint);
                proxyServer.Start();
                proxyEndpoint = new IPEndPoint(internalProxyEndpoint.IpAddress, internalProxyEndpoint.Port);
                Logger.Info($"Internal Proxy Endpoint: {proxyEndpoint}");
            }
            else
            {
                if (! IPEndPoint.TryParse(externalProxyEndpoint, out proxyEndpoint))
                {
                    Logger.Fatal("Invalid value for --external-proxy-endpoint: An invalid IP EndPoint was specified.");
                    Environment.Exit(-1);
                }
                else
                {
                    Logger.Info($"External Proxy Endpoint: {proxyEndpoint}");
                }
            }
            
            //if (tunnelEndpoint == null)
            //{
            //    tunnelEndpoint = Consts.TUNNEL_ENDPOINT;
            //}

            if (!IPEndPoint.TryParse(tunnelEndpoint, out IPEndPoint endPointTtunnel))
            {
                Logger.Fatal("Invalid value for --tunnel-endpoint: An invalid IP EndPoint was specified.");
                Environment.Exit(-1);
            }
            Logger.Info($"Tunnel Endpoint {endPointTtunnel}");



            //Console.WriteLine($"The value for --bool-option is: {tunnelPort}");
            //Console.WriteLine($"The value for --file-option is: {clientCert?.Exists.ToString() ?? "null"}");
            Stream TunnelStream = null;

            try
            {
                TunnelStream = TunnelConnection.ConnectToServer(endPointTtunnel, key);
                if (TunnelStream == null)
                {
                    Logger.Info("Can't connect to server...");
                    return;
                }

                Thread PrintConnectionsCount = new Thread(() =>
                {
                    while (true)
                    {
                        Logger.Info("Number of connections: " + ConnectionsDictionary.GetNumberOfConnections().ToString());
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
                    Logger.Debug("Recived message from tunnel, type: " + ((MessageType)buffer[0]).ToString());
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
                    Logger.Debug("Recived message from tunnel, type: " + ((MessageType)buffer[0]).ToString() + ", Id : #" + message.ID);
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
                            Logger.Debug("Sent message to #" + message.ID.ToString());
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
    }
}
