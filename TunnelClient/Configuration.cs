using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TunnelClient
{
    class Configuration
    {

        private static IConfigurationRoot ConfigurationRoot { get; set; }
        
        public static EndpointSetting TunnelServerEndpoint { private set;  get; } = new();
        public static EndpointSetting OutgoingProxyEndpoint { private set; get; } = new();
        public static EndpointSetting InternalProxyEndpoint { private set; get; } = new();
        public static string OutgoingProxyCredentials { private set; get; } = null;
        public static string Key { private set; get; } = null;
        public static bool UseSystemProxyConfig { private set; get; } = false;
        
        public static void Init(string[] args)
        {

            ConfigurationRoot = new ConfigurationBuilder()
                    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                    //.AddEnvironmentVariables()
                    .AddCommandLine(args)
                    .Build();


            ConfigurationRoot.GetSection("TunnelServerEndpoint").Bind(TunnelServerEndpoint);
            ConfigurationRoot.GetSection("OutgoingProxyEndpoint").Bind(OutgoingProxyEndpoint);
            ConfigurationRoot.GetSection("InternalProxyEndpoint").Bind(InternalProxyEndpoint);
            Key = ConfigurationRoot["Key"];

            // UseSystemProxyConfig determines if the machine proxy settings should be applied 
            // to in order to determine the outgoing proxy according to the host:port of the 
            // connection's destination 
            UseSystemProxyConfig = ConfigurationRoot.GetValue<bool>("UseSystemProxyConfig");

            //If outgoingProxy credentials are available, prepare it for basic authorization scheme 
            string outgoingProxyUsername = ConfigurationRoot["OutgoingProxyUsername"];
            string outgoingProxyPassword = ConfigurationRoot["OutgoingProxyPassword"];
            if (!string.IsNullOrEmpty(outgoingProxyUsername) && string.IsNullOrEmpty(outgoingProxyPassword))
            {
                OutgoingProxyCredentials = $"{outgoingProxyUsername}:{outgoingProxyPassword}";
            }
        }

    }
}
