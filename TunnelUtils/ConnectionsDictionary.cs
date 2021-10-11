using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace TunnelUtils
{
    public class ConnectionsDictionary
    {
        private static ReaderWriterLockSlim _locker = new ReaderWriterLockSlim();
        private static Dictionary<int, ConnectionHandler> _connections = new Dictionary<int, ConnectionHandler>();

        public static void Add(ConnectionHandler connection)
        {
            _locker.EnterWriteLock();
            _connections.Add(connection.ConnectionID, connection);
            _locker.ExitWriteLock();

            Logger.Debug("Added new connection with id " + connection.ConnectionID.ToString() + " to the dictionary");
        }

        public static void Remove(int id, bool onReadLock = false)
        {
            if (!onReadLock)
            {
                _locker.EnterUpgradeableReadLock();
            }
            if (_connections.ContainsKey(id))
            {
                _connections[id].CloseConnection();
                _locker.EnterWriteLock();
                _connections.Remove(id);
                _locker.ExitWriteLock();
                if (!onReadLock)
                {
                    _locker.ExitUpgradeableReadLock();
                }
            }
            else
            {
                if (!onReadLock)
                {
                    _locker.ExitUpgradeableReadLock();
                }
                Logger.Debug("Error: Cant remove connection with id " + id.ToString());
            }
            Logger.Debug("Removed #" + id.ToString());

        }

        public static void RemoveAllConnections()
        {
            _locker.EnterUpgradeableReadLock();
            foreach (int key in _connections.Keys)
            {
                Remove(key, true);
            }
            _locker.ExitUpgradeableReadLock();
            Logger.Debug("Removed all connections");
        }

        public static void SendMessage(Message message)
        {
            _locker.EnterReadLock();
            if (_connections.ContainsKey(message.ID))
            {
                ConnectionHandler tmp = _connections[message.ID];
                _locker.ExitReadLock();
                tmp.Write(message.Data.Value);
            }
            else
            {
                _locker.ExitReadLock();
                Logger.Debug("Error: Cant message connection with id " + message.ID.ToString());
            }
            Logger.Trace("Sent message to connection with id " + message.ID.ToString());
        }

        public static int GetNumberOfConnections()
        {
            return _connections.Count;
        }
    }
}
