using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TunnelUtils
{
    public static class GeneralUtils
    {

        public static string GetHexString(byte[] buff, int nBytes = -1)
        {
            int buffLen = nBytes > 0 ? nBytes : buff.Length;
            var sb = new StringBuilder(buffLen * 2);
            if (buff != null)
            {
                foreach (byte b in buff)
                {
                    sb.Append(b.ToString("x2"));
                    buffLen--;
                    if (buffLen == 0) break;
                }
            }
            return sb.ToString();
        }



        public static byte[] GetByteArray(string hexString)
        {
            if (hexString.Length % 2 != 0)
            {
                return null;
            }

            int buffLen = hexString.Length / 2;
            byte[] arr = new byte[buffLen];

            for (int i = 0; i < buffLen; i++)
            {
                int firstDigit = GetHexVal(hexString[i * 2]);
                int secondDigit = GetHexVal(hexString[i * 2 + 1]);
                if (firstDigit < 0 || secondDigit < 0)
                {
                    return null;
                }
                arr[i] = (byte)((firstDigit << 4) + secondDigit);
            }

            return arr;
        }


        public static int GetHexVal(char hexChar)
        {
            int val = hexChar;
            if (val >= '0' && val <= '9')
            {
                //Numeric
                return val - '0';
            }
            if (val >= 'A' && val <= 'F')
            {
                //Capital
                return 10 + val - 'A';
            }
            if (val >= 'a' && val <= 'f')
            {
                //Low
                return 10 + val - 'a';
            }
            return -1;
        }

        public static void PrintConnectionCount()
        {
            //Print the connections count ever 7.5 seconds
            try
            {
                Thread PrintConnectionsCount = new Thread(() =>
                {
                    while (true)
                    {
                        Logger.Debug("Number of connections: " + ConnectionsDictionary.GetNumberOfConnections().ToString());
                        Thread.Sleep(7500);
                    }
                });

                PrintConnectionsCount.Start();
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Failed to start the PrintConnectionsCount thread");
            }
        }
    }
}
