using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using TunnelUtils;

namespace TunnelServer
{
    class Program
    {

        static void Main(string[] args)
        {

            // Create a root command with some options
            var rootCommand = new RootCommand
            {
                new Option<int>(new string[] {"--proxy-port","--pp" }, getDefaultValue: () => 0)
                {
                    Description = "Proxy listen port",
                },
                new Option<int>(new string[] {"--tunnel-port","--tp" })
                {
                    Description = "Tunnel listen port",
                    IsRequired = true,
                },
                new Option<string>(new string[] {"--key-hash","--kh" }, getDefaultValue: () => Consts.TEST_SECRET_KEY_HASH)
                {
                    Description = "If provided, the tunnel server will validate the client's key",
                }
            };

            rootCommand.Description = "Tunnel Client";
            //Note that the parameters of the handler method are matched according to the names of the options
            rootCommand.Handler = CommandHandler.Create<int, int, string>(StartServer);


            var genKeyCommand = new Command("gen-key")
            {
                new Option<int>(new string[] {"--key-length","--l" }, getDefaultValue: () => 40, description: "Key length (bytes)")
            };
            genKeyCommand.Description = "Generate a random secret key and its SHA256 value";
            genKeyCommand.Handler = CommandHandler.Create<int>(GenKey);
            rootCommand.AddCommand(genKeyCommand);


            rootCommand.Invoke(args);


        }


        //Generate a random secret key and its SHA256 value
        static void GenKey(int keyLength)
        {
            if (keyLength > 100)
            {
                Logger.Fatal("Key length too big. (maximum is 100)");
                Environment.Exit(-1);
            }
            byte[] keyBytes = new byte[keyLength];
            using (var rng = RandomNumberGenerator.Create())
            {

                rng.GetBytes(keyBytes);
                Console.WriteLine("Key: " + GeneralUtils.GetHexString(keyBytes));
            }
            using (var hasher = SHA256.Create())
            {
                var keyHash = hasher.ComputeHash(keyBytes);
                Console.WriteLine("KeyHash: " + GeneralUtils.GetHexString(keyHash));
            }
        }

        static void StartServer(int tunnelPort, int proxyPort, string keyHash)
        {
            Logger.Info($"Tunnel port: {tunnelPort}");
            Logger.Info($"Proxy port: {proxyPort}");
            Logger.Info($"Using key hash: {keyHash}");

            Stream TunnelStream = null;
            ServerLitseners Listeners = null;

            try
            {
                Listeners = new ServerLitseners(tunnelPort, proxyPort, keyHash);
                TunnelStream = Listeners.LitsenForTunnelClient();

                Listeners.LitsenForConnections();

                GeneralUtils.PrintConnectionCount();

                while (true)
                {
                    byte[] buffer;
                    try
                    {
                        buffer = WebSocketUtils.ParseFrame(TunnelStream, mask: true);
                    }
                    catch (Exception)
                    {
                        TunnelStream = Listeners.NewTunnelConnection();
                        continue;
                    }

                    Logger.Debug("Recived message from tunnel, type: " + ((MessageType)buffer[0]).ToString());

                    Message message = Message.Recive(buffer);
                    if (message.Type == MessageType.TunnelClosed)
                    {
                        Logger.Info("Tunnel disconnected...");
                        TunnelStream = Listeners.NewTunnelConnection();
                        continue;
                    }
                    Logger.Debug("Recived message from tunnel, type: " + ((MessageType)buffer[0]).ToString() + ", Id : #" + message.ID);
                    switch (message.Type)
                    {

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
            catch (SocketException e)
            {
                Logger.Error(e,"Failure in main");
                return;
            }

            finally
            {
                if (Listeners != null)
                {
                    Listeners.CloseAll();
                }
            }
        }
    }
}
