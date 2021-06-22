using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TunnelUtils;

namespace TestTunnelUtils
{
    public class TestWebSocketUtils
    {
        [SetUp]
        public void Setup()
        {
        }


        internal static bool ClientHandshake(int serverPort)
        {
            Thread.Sleep(100); //Wait for the listener

            Socket clientSSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            clientSSocket.Connect(IPAddress.Loopback, serverPort);
            Logger.Info("Connected to tunnel server");

            NetworkStream stream = new NetworkStream(clientSSocket, true);
            return WebSocketUtils.ClientHandshake(stream, "localhost", Consts.TEST_SECRET_KEY);
        }


        [Test]
        public void TestWebSocketHandshake()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = (listener.LocalEndpoint as IPEndPoint).Port;
            bool clientReturnCode = false;
            Thread clientThread = new Thread(() =>
            {
                clientReturnCode = ClientHandshake(port);
            });
            clientThread.Start();
            TcpClient client = listener.AcceptTcpClient();
            NetworkStream stream = client.GetStream();
            bool rc = WebSocketUtils.ServerHandshake(stream, Consts.TEST_SECRET_KEY_HASH);
            Assert.IsTrue(rc);
            //Wait for the client thread to complete
            Thread.Sleep(100); 
            Assert.AreEqual(ThreadState.Stopped, clientThread.ThreadState);
            Assert.IsTrue(clientReturnCode);
        }



        [Test]
        public void TestWebSocketHandshake_WrongToken()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = (listener.LocalEndpoint as IPEndPoint).Port;
            bool clientReturnCode = false;
            Thread clientThread = new Thread(() =>
            {
                clientReturnCode = ClientHandshake(port);
            });
            clientThread.Start();
            TcpClient client = listener.AcceptTcpClient();
            NetworkStream stream = client.GetStream();
            bool rc = WebSocketUtils.ServerHandshake(stream, "wrongToken");
            Assert.IsFalse(rc);
            //Wait for the client thread to complete
            Thread.Sleep(100);
            Assert.AreEqual(ThreadState.Stopped, clientThread.ThreadState);
            Assert.IsFalse(clientReturnCode);
        }


        [TestCase(true)]
        [TestCase(false)]
        public void TestWebSocketFramePrefix(bool mask)
        {
            using (var ms = new MemoryStream())
            {
                byte[] data = Encoding.ASCII.GetBytes("abcdefghij");
                long expectedLength = data.Length;
                WebSocketFrame wsf = new WebSocketFrame(expectedLength, mask);
                wsf.AppendData(data);
                ms.Write(wsf.GetFrameBuffer());
                ms.Seek(0, SeekOrigin.Begin);

                byte[] parsedData = WebSocketUtils.ParseFrame(ms, mask);
                Assert.AreEqual(parsedData, data);
                ms.Seek(0, SeekOrigin.Begin);
                string expectedExceptionMessage = mask ? "Expecting non-masked message but got masked one"
                    : "Expecting masked message and got non-masked one";
                Assert.Throws<Exception>(() => WebSocketUtils.ParseFrame(ms, !mask), expectedExceptionMessage);
            }
        }

        [TestCase(12, true)]
        [TestCase(127, true)]
        [TestCase(128, true)]
        [TestCase(200, true)]
        [TestCase(1200, true)]
        [TestCase(0x10000, true)]
        [TestCase(12, false)]
        [TestCase(127, false)]
        [TestCase(128, false)]
        [TestCase(200, false)]
        [TestCase(1200, false)]
        [TestCase(0x10000, false)]
        public void TestWebSocketFramePrefixVariousLengths(long expectedLength, bool mask)
        {
            using (var ms = new MemoryStream())
            {
                byte[] data = new byte[expectedLength];
                WebSocketFrame wsf = new WebSocketFrame(expectedLength, mask);
                wsf.AppendData(data);
                ms.Write(wsf.GetFrameBuffer());
                ms.Seek(0, SeekOrigin.Begin);
                
                byte[] parsedData = WebSocketUtils.ParseFrame(ms, mask);
                Assert.AreEqual(parsedData, data);
            }
        }


    }
}