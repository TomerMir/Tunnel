using System;
using System.IO;
using System.Net.Sockets;

namespace TunnelUtils
{
    public class Message
    {
        public ArraySegment<byte>? Data { get; }
        public int ID { get; }
        public MessageType Type { get; }


        public Message(MessageType type, int id = 0, ArraySegment<byte>? data = null)
        {
            Data = data;
            ID = id;
            Type = type;
        }



        public byte[] Serialize(bool mask)
        {
            try
            {
                int messageSize = sizeof(MessageType) + sizeof(int) + (Data == null ? 0 : Data.Value.Count);
                WebSocketFrame wsf = new WebSocketFrame(messageSize, mask);
                wsf.AppendData((byte)Type);
                byte[] idBytes = BitConverter.GetBytes(ID);
                wsf.AppendData(idBytes);
                if (Data != null)
                {
                    wsf.AppendData(Data.Value);
                }
                Logger.Debug("Serialized message");
                return wsf.GetFrameBuffer();
            }
            catch (Exception ex)
            {
                Logger.Debug("Error: failed to serialize message : "+ ex.ToString());
                throw new Exception("Error: failed to serialize message : " + ex.ToString());
            }
        }

        public static Message Recive(byte[] buffer)
        {
            try
            {
                //First message byte is the messageType,
                //Following 4 bytes are the ID of the message (which is TunnelServer port)
                MessageType type = (MessageType)buffer[0];
                int id = BitConverter.ToInt32(new ArraySegment<byte>(buffer,1,4));
                if (type == MessageType.Message)
                {
                    return new Message(MessageType.Message, id, new ArraySegment<byte>(buffer, 5, buffer.Length - 5));
                }
                return new Message(type, id);
            }
            catch (Exception ex)
            {
                Logger.Debug("Faliure: failed to recive message from tunnel : " + ex.ToString());
                return new Message(MessageType.TunnelClosed);
            }
        }
    }

    


    public enum MessageType : byte
    {
        Open,
        Close,
        Message,
        TunnelClosed
    }
}
