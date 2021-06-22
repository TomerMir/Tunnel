using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TunnelUtils
{
    public class ConnectionHandler
    {
        public static readonly object locker = new object();

        private byte[] _buffer = new byte[20000];
        private AsyncCallback _asyncReceiveCallback = new AsyncCallback(ProcessReceiveResults);
        private Stream _connectionNetworkStream = null;
        private Stream _tunnelStream = null;
        bool _isClient = false;

        public int ConnectionID { get; }

        public ConnectionHandler(Socket browserConnection, Stream tunnelSocket)
        {
            try
            {
                _isClient = false;
                ConnectionID = (browserConnection.RemoteEndPoint as IPEndPoint).Port ;
                Logger.Info("#" + ConnectionID.ToString() + " Connected");
                Logger.Debug("Connection #" + ConnectionID.ToString() + " is established and awaiting data...");
                _connectionNetworkStream = new NetworkStream(browserConnection, true);
                Logger.Debug("NetworkStream is OK...");
                _tunnelStream = tunnelSocket;
                Message message = new Message(MessageType.Open, ConnectionID);
                lock (locker)
                {
                    _tunnelStream.Write(message.Serialize(_isClient));
                }
                Logger.Debug($"Sent open connection message to client #{ConnectionID}");
                ConnectionsDictionary.Add(this);
                _connectionNetworkStream.BeginRead(_buffer, 0, _buffer.Length, _asyncReceiveCallback, this);
                Logger.Debug($"Started BeginRead() #{ConnectionID}");
            }
            catch (Exception e)
            {
                ConnectionsDictionary.Remove(ConnectionID);
                Logger.Error(e, $"Exception when trying to read from a new connection Id: ({ConnectionID})");
            }
        }

        public ConnectionHandler(IPEndPoint externalProxyEndpoint, Stream tunnelSocket, int id)
        {
            _isClient = true;
            ConnectionID = id;
            _tunnelStream = tunnelSocket;
            bool addedToDict = false;
            try
            {
                Logger.Debug($"Connecting proxy #{ConnectionID}");
                Socket connectionSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
                connectionSocket.Connect(externalProxyEndpoint);
                _connectionNetworkStream = new NetworkStream(connectionSocket, true);
                Logger.Debug($"Connecting proxy #{ConnectionID} completed successfully...");
                ConnectionsDictionary.Add(this);
                addedToDict = true;
                _connectionNetworkStream.BeginRead(_buffer, 0, _buffer.Length, _asyncReceiveCallback, this);
                Logger.Debug($"Started BeginRead() #{ConnectionID}");

            }
            catch (Exception ex)
            {
                Logger.Error(ex, $"Failed to open outgoing connection {id}  addedToDict: {addedToDict}");
                if (addedToDict)
                {
                    ConnectionsDictionary.Remove(ConnectionID);
                }
                ClosePeer();
            }

        }


        private void ClosePeer()
        {
            Logger.Debug("Closing peer for connection #" + ConnectionID);
            Message closingMessage = new Message(MessageType.Close, ConnectionID);
            lock (locker)
            {
                _tunnelStream.Write(closingMessage.Serialize(_isClient));
            }
        }

        static void ProcessReceiveResults(IAsyncResult ar)
        {
            ConnectionHandler handler = (ConnectionHandler)ar.AsyncState;

            try
            {
                int BytesRead = 0;
                BytesRead = handler._connectionNetworkStream.EndRead(ar);

                Logger.Debug("Connection #" + handler.ConnectionID.ToString() + " received " + BytesRead.ToString() + " bytes.");

                if (BytesRead == 0)
                {
                    Logger.Debug("Connection #" + handler.ConnectionID.ToString() + " is closing.");
                    handler.ClosePeer();
                    Logger.Debug("Sent closing message to tunnel");
                    ConnectionsDictionary.Remove(handler.ConnectionID);
                    Logger.Debug("Deleted connection from dictionary");
                }

                else
                {
                    Message message = new Message(MessageType.Message, handler.ConnectionID, new ArraySegment<byte>(handler._buffer, 0,BytesRead));
                    lock (locker)
                    {
                        handler._tunnelStream.Write(message.Serialize(handler._isClient));
                    }
                    Logger.Debug("Forwarded message to the tunnel");

                    Logger.Debug("Started reading again");
                    handler._connectionNetworkStream.BeginRead(handler._buffer, 0, handler._buffer.Length, handler._asyncReceiveCallback, handler);
                }
            }
            catch (Exception e)
            {
                Logger.Debug("Faliure in ProcessReceiveResults: " + e.Message);
                handler.ClosePeer();
                ConnectionsDictionary.Remove(handler.ConnectionID);
            }
        }

        public void Write(ArraySegment<byte> bufferToWrite)
        {
            try
            {
                _connectionNetworkStream.Write(bufferToWrite);
                Logger.Debug("Forwarded message from tunnel to connection with ID " + ConnectionID.ToString());
            }
            catch (Exception ex)
            {
                ConnectionsDictionary.Remove(ConnectionID);
                Logger.Debug("Tried to forward message from tunnel to connection with ID " + ConnectionID.ToString() + " but " + ex.ToString());
            }
        }

        public void CloseConnection()
        {
            try
            {
                _connectionNetworkStream.Close();
                Logger.Debug("Closed connection with id " + ConnectionID.ToString());
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Trying to close a closed connection");
            }
        }
    }
}
