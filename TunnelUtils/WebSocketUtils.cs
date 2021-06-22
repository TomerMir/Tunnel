using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;

namespace TunnelUtils
{
    

    public static class WebSocketUtils
    {
        internal static RandomNumberGenerator _rng = null;
        internal static RandomNumberGenerator RNG
        {
            get
            {
                if (_rng == null)
                {
                    _rng = RandomNumberGenerator.Create();
                }
                return _rng;
            }
        }
        internal static void GetRandomBytes(Span<byte> arr)
        {
            lock (RNG)
            {
                RNG.GetBytes(arr);
            }
        }

        internal static uint GetRandomMask()
        {
            byte[] buff = new byte[4];
            _rng.GetBytes(buff);
            return BitConverter.ToUInt32(buff);
        }

        enum ClientHeadersEnum
        {
            RequestLine = 0,
            Host = 1,
            Connection = 2,
            WebSocketKey = 3,
            WebSocketVer = 4,
            Authorization = 5
        };

        static Dictionary<string, ClientHeadersEnum> ClientHeadersMapping = new Dictionary<string, ClientHeadersEnum>
        {
            {"Host", ClientHeadersEnum.Host },
            {"Connection", ClientHeadersEnum.Connection },
            {"Sec-WebSocket-Key", ClientHeadersEnum.WebSocketKey },
            {"Sec-WebSocket-Version", ClientHeadersEnum.WebSocketVer },
            {"Authorization", ClientHeadersEnum.Authorization },
        };

        enum ServerHeadersEnum
        {
            StatusLine = 0,
            Upgrade = 1,
            Connection = 2,
            WebSocketAccept = 3,
        };

        static Dictionary<string, ServerHeadersEnum> ServerHeadersMapping = new Dictionary<string, ServerHeadersEnum>
        {
            {"Upgrade", ServerHeadersEnum.Upgrade },
            {"Connection", ServerHeadersEnum.Connection },
            {"Sec-WebSocket-Accept", ServerHeadersEnum.WebSocketAccept }
        };


        static byte CR = 0x0D;
        static byte LF = 0x0A;
        static byte[] CRLF = new byte[] { CR, LF };
        /*
          Opcode:  4 bits

          Defines the interpretation of the "Payload data".  If an unknown
          opcode is received, the receiving endpoint MUST _Fail the
          WebSocket Connection_.  The following values are defined.
         */

        enum FrameOpcode : byte
        {
            Continuation = 0x0,
            Text = 0x1,
            Binary = 0x2,
            Reserved1 = 0x3,
            Reserved2 = 0x4,
            Reserved3 = 0x5,
            Reserved4 = 0x6,
            Reserved5 = 0x7,
            ConnectionClose = 0x8,
            Ping = 0x9,
            Pong = 0xA
        }



        enum EndOfHeadersState : byte { Start, CR, CRLF, CRLFCR };

        const int HEADERS_TOO_LARGE = -1;
        const int CONNECTION_CLOSED = -2;

        static int ReadHeadersSection(Stream st, byte[] headersBuff)
        {
            int buffLength = headersBuff.Length;
            EndOfHeadersState state = EndOfHeadersState.Start;
            for (int i = 0; i < headersBuff.Length; i++)
            {
                int n = st.Read(headersBuff, i, 1);
                if (n == 0) return CONNECTION_CLOSED;
                switch (state)
                {
                    case EndOfHeadersState.Start:
                        if (headersBuff[i] == CR) state = EndOfHeadersState.CR;
                        else if (headersBuff[i] == LF) state = EndOfHeadersState.CRLF;
                        //else remain in start
                        break;
                    case EndOfHeadersState.CR:
                        if (headersBuff[i] == LF) state = EndOfHeadersState.CRLF;
                        else if (headersBuff[i] != CR) state = EndOfHeadersState.Start;
                        //else remain in CR
                        break;
                    case EndOfHeadersState.CRLF:
                        if (headersBuff[i] == CR) state = EndOfHeadersState.CRLFCR;
                        else if (headersBuff[i] == LF) return i;
                        else state = EndOfHeadersState.Start;
                        break;
                    case EndOfHeadersState.CRLFCR:
                        if (headersBuff[i] == LF) return i;
                        else if (headersBuff[i] != CR) state = EndOfHeadersState.Start;
                        //else remain in CRLFCR
                        break;
                }
            }
            return HEADERS_TOO_LARGE;
        }



        static string GetFirstLine(ArraySegment<byte> buff, out int iFirstLineEnd)
        {
            int i;
            string firstLine = null;
            for (i = 0; i < buff.Count; i++)
            {
                if (buff[i] == CR || buff[i] == LF)
                {
                    //Get the first line as string
                    firstLine = Encoding.ASCII.GetString(buff.AsSpan(0, i));
                    break;
                }
            }
            for (iFirstLineEnd = i + 1; iFirstLineEnd < buff.Count; iFirstLineEnd++)
            {
                if (buff[iFirstLineEnd] != CR && buff[iFirstLineEnd] != LF) break;
            }

            return firstLine;
        }


        static Tuple<string, string> GetNextHeader(ArraySegment<byte> buff, out int iFirstLineEnd)
        {
            int i0, i1;
            string header = null;
            for (i0 = 0; i0 < buff.Count; i0++)
            {
                if (buff[i0] != CR && buff[i0] != LF)
                {
                    break;
                }
            }
            for (i1 = i0; i1 < buff.Count; i1++)
            {
                if (buff[i1] == CR || buff[i1] == LF)
                {
                    //Get the first line as string
                    header = Encoding.ASCII.GetString(buff.AsSpan(i0, i1));
                    break;
                }
            }

            for (iFirstLineEnd = i1 + 1; iFirstLineEnd < buff.Count; iFirstLineEnd++)
            {
                if (buff[iFirstLineEnd] != CR && buff[iFirstLineEnd] != LF) break;
            }

            if (header == null)
            {
                return null;
            }

            int iSep = header.IndexOf(':');
            string headerName = header.Substring(0, iSep).Trim();
            string headerValue = header.Substring(iSep + 1).Trim();
            return Tuple.Create(headerName, headerValue);
        }



        public static bool ClientHandshake(Stream stream, string host, string token)
        {
            byte[] WSkey = new byte[16];
            GetRandomBytes(WSkey);
            string WSKeyStr = Convert.ToBase64String(WSkey);

            StringBuilder sb = new StringBuilder(2048);
            sb.Append("GET /tunnel HTTP/1.1\r\n");
            sb.Append($"Host: {host}\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append($"Sec-WebSocket-Key: {WSKeyStr}\r\n");
            sb.Append("Sec-WebSocket-Version: 13\r\n");
            sb.Append($"Authorization: Bearer {token}\r\n");
            sb.Append("\r\n");

            //Write the handshake request
            stream.Write(Encoding.ASCII.GetBytes(sb.ToString()));

            //Read the handshake response
            const int RespBuffSize = 4096;
            byte[] respBuffer = new byte[RespBuffSize];

            int iEndOfHeaders = ReadHeadersSection(stream, respBuffer);
            if (iEndOfHeaders == -1)
            {
                //Response headers too large
                return false;
            }

            string firstLine = GetFirstLine(new ArraySegment<byte>(respBuffer, 0, iEndOfHeaders), out int iNextHeader);
            if (firstLine != "HTTP/1.1 101 Switching Protocols")
            {
                //Request was not accepted by the server
                return false;
            }


            bool WebSocketAcceptReceived = false;
            bool ConnectionReceived = false;
            bool UpgradeReceived = false;

            while (true)
            {
                int prevHeader = iNextHeader;
                Tuple<string, string> header = GetNextHeader(new ArraySegment<byte>(respBuffer, iNextHeader, iEndOfHeaders), out iNextHeader);
                iNextHeader += prevHeader;

                if (header == null) break;
                if (ServerHeadersMapping.TryGetValue(header.Item1, out ServerHeadersEnum respHeader))
                {
                    switch (respHeader)
                    {
                        case ServerHeadersEnum.WebSocketAccept:
                            //Verify that the accept contains the right value
                            string expectedAccept = GetSecWebSocketAccept(WSKeyStr);
                            if (expectedAccept != header.Item2)
                            {
                                //The accept does not match the key that was sent in the request
                                return false;
                            }
                            WebSocketAcceptReceived = true;
                            break;
                        case ServerHeadersEnum.Connection:
                            if (header.Item2 != "Upgrade")
                            {
                                return false;
                            }
                            ConnectionReceived = true;
                            break;
                        case ServerHeadersEnum.Upgrade:
                            if (header.Item2 != "websocket")
                            {
                                return false;
                            }
                            UpgradeReceived = true;
                            break;
                    }
                }
            }
            if (!WebSocketAcceptReceived || !ConnectionReceived || !UpgradeReceived)
            {
                //Missing required header
                return false;
            }
            return true;
        }


        static byte[] GetErrorResp(int respStatus)
        {
            string respLine;
            switch (respStatus)
            {
                case 413:
                    respLine = "HTTP/1.1 431 Request Header Fields Too Large";
                    break;
                case 401:
                    respLine = "HTTP/1.1 401 Unauthorized";
                    break;
                case 400:
                default:
                    respLine = "HTTP/1.1 400 Bad Request";
                    break;
            }
            return Encoding.ASCII.GetBytes(respLine + "\r\n\r\n");

        }


        public static bool ServerHandshake(Stream stream, string tokenHash)
        {
            //Read the handshake request
            const int ReqBuffSize = 4096;
            byte[] reqBuffer = new byte[ReqBuffSize];

            int iEndOfHeaders = ReadHeadersSection(stream, reqBuffer);
            if (iEndOfHeaders == HEADERS_TOO_LARGE)
            {
                //Response headers too large
                stream.Write(GetErrorResp(413));
                return false;
            }
            if (iEndOfHeaders == CONNECTION_CLOSED)
            {
                return false;
            }

            string firstLine = GetFirstLine(new ArraySegment<byte>(reqBuffer, 0, iEndOfHeaders), out int iNextHeader);
            if (firstLine != "GET /tunnel HTTP/1.1")
            {
                //Get request is expected
                stream.Write(GetErrorResp(400));
            }


            bool WebSocketKeyReceived = false;
            bool WebSocketVerReceived = false;
            bool ConnectionReceived = false;
            bool AuthorizationReceived = false;
            string acceptKey = null;
            while (true)
            {
                int prevHeader = iNextHeader;
                Tuple<string, string> header = GetNextHeader(new ArraySegment<byte>(reqBuffer, iNextHeader, iEndOfHeaders), out iNextHeader);
                iNextHeader += prevHeader;

                if (header == null) break;
                if (ClientHeadersMapping.TryGetValue(header.Item1, out ClientHeadersEnum respHeader))
                {
                    switch (respHeader)
                    {
                        case ClientHeadersEnum.WebSocketKey:
                            //Calculate the key hash (for the accept)
                            acceptKey = GetSecWebSocketAccept(header.Item2);
                            WebSocketKeyReceived = true;
                            break;
                        case ClientHeadersEnum.Connection:
                            if (header.Item2 != "Upgrade")
                            {
                                stream.Write(GetErrorResp(400));
                                return false;
                            }
                            ConnectionReceived = true;
                            break;
                        case ClientHeadersEnum.WebSocketVer:
                            if (header.Item2 != "13")
                            {
                                stream.Write(GetErrorResp(400));
                                return false;
                            }
                            WebSocketVerReceived = true;
                            break;
                        case ClientHeadersEnum.Authorization:
                            if (! header.Item2.StartsWith("Bearer "))
                            {
                                stream.Write(GetErrorResp(401));
                                return false;
                            }
                            string tokenStr = header.Item2.Substring("Bearer ".Length);
                            byte[] tokenBytes = GeneralUtils.GetByteArray(tokenStr.Trim());
                            using (var hasher = SHA256.Create())
                            {
                                var keyHash = hasher.ComputeHash(tokenBytes);
                                if (GeneralUtils.GetHexString(keyHash) != tokenHash)
                                {
                                    stream.Write(GetErrorResp(401));
                                    return false;
                                }
                            }

                            AuthorizationReceived = true;
                            break;
                    }
                }
            }

            if (!AuthorizationReceived)
            {
                stream.Write(GetErrorResp(401));
                return false;
            }

            if (!WebSocketKeyReceived || !WebSocketVerReceived || !ConnectionReceived)
            {
                //Missing required header
                stream.Write(GetErrorResp(400));
                return false;
            }

            //Create the server response
            StringBuilder sb = new StringBuilder(2048);
            sb.Append("HTTP/1.1 101 Switching Protocols\r\n");
            sb.Append($"Upgrade: websocket\r\n");
            sb.Append("Connection: Upgrade\r\n");
            sb.Append($"Sec-WebSocket-Accept: {acceptKey}\r\n");
            sb.Append("\r\n");

            //Write the handshake request
            stream.Write(Encoding.ASCII.GetBytes(sb.ToString()));

            return true;
        }




        internal static string GetSecWebSocketAccept(string clientKeyStr)
        {
            // SHA1 is aused according to RFC 6455 only for hashing. 
            using (SHA1 sha1 = SHA1.Create())
            {
                string concatStr = string.Concat(clientKeyStr, "258EAFA5-E914-47DA-95CA-C5AB0DC85B11");
                return Convert.ToBase64String(sha1.ComputeHash(Encoding.UTF8.GetBytes(concatStr)));
            }
        }


        /*
      0                   1                   2                   3
      0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1 2 3 4 5 6 7 8 9 0 1
     +-+-+-+-+-------+-+-------------+-------------------------------+
     |F|R|R|R| opcode|M| Payload len |    Extended payload length    |
     |I|S|S|S|  (4)  |A|     (7)     |             (16/64)           |
     |N|V|V|V|       |S|             |   (if payload len==126/127)   |
     | |1|2|3|       |K|             |                               |
     +-+-+-+-+-------+-+-------------+ - - - - - - - - - - - - - - - +
     |     Extended payload length continued, if payload len == 127  |
     + - - - - - - - - - - - - - - - +-------------------------------+
     |                               |Masking-key, if MASK set to 1  |
     +-------------------------------+-------------------------------+
     | Masking-key (continued)       |          Payload Data         |
     +-------------------------------- - - - - - - - - - - - - - - - +
     :                     Payload Data continued ...                :
     + - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - +
     |                     Payload Data continued ...                |
     +---------------------------------------------------------------+
 */

        public static int GetFrameSize(long messageLength, bool mask)
        {
            int frameSize = 2;  //This is for the first 2 bytet 
            if (messageLength > 0x7D && messageLength <= 0xFFFF)
            {
                frameSize += 2;
            } 
            else if (messageLength > 0xFFFF)
            {
                frameSize += 8;
            }

            if (mask)
            {
                frameSize += 4;  //4 bytes for the mask
            }
            return frameSize;
        }





        public static void ReadFromStreamThrow(Stream st, byte[] buff, int offset, int length)
        {
            bool rc = ReadFromStream(st, buff, offset, length);
            if (!rc) throw new Exception("Connection was closed while expecting more data");
        }
        public static bool ReadFromStream(Stream st, byte[] buff, int offset, int length)
        {
            Debug.Assert(buff.Length >= length);
            int totalRead = 0;
            do
            {
                int nBytes = st.Read(buff, offset + totalRead, length - totalRead);
                if (nBytes == 0)
                {
                    //Connection was closed
                    return false;
                }
                totalRead += nBytes;
            } while (totalRead < length);
            return true;
        }

        public static byte[] ParseFrame(Stream inStream, bool mask)
        {
            byte[] frameData;
            byte[] maskBytes = null;
            byte[] buff = new byte[10];
            ReadFromStreamThrow(inStream, buff, 0, 2);

            //Fin = 1; Opcode: Data - binary (2)
            if (buff[0] != 0x82) throw new Exception("Invalid frame prefix");

            byte b1 = buff[1];
            if ((b1 & 0x80) != 0)
            {
                maskBytes = new byte[4];
                b1 = (byte)(buff[1] & ~0x80);
            }

            if (mask && maskBytes == null)
            {
                throw new Exception("Expecting masked message and got non-masked one");
            }

            if (!mask && maskBytes != null)
            {
                throw new Exception("Expecting non-masked message but got masked one");
            }

            if (b1 <= 0x7D)
            {
                frameData = new byte[b1];
            }
            else if (b1 == 0x7E)
            {
                //frame-payload-length-16
                ReadFromStreamThrow(inStream, buff, 0, 2);
                var length = BitConverter.ToUInt16(new ReadOnlySpan<byte>(buff, 0, 2));
                frameData = new byte[length];
            }
            else
            {
                //frame-payload-length-63
                ReadFromStreamThrow(inStream, buff, 0, 8);
                var length = BitConverter.ToInt64(new ReadOnlySpan<byte>(buff, 0, 8));
                //Limit the frame size to 10MiB
                if (length > 10485760) throw new Exception("Frame too big");
                frameData = new byte[length];
            }
            if (maskBytes != null)
            {
                //Read the mask
                ReadFromStreamThrow(inStream, maskBytes, 0, 4);
            }

            ReadFromStreamThrow(inStream, frameData, 0, frameData.Length);
            if (maskBytes != null)
            {
                MaskBuffer(frameData, maskBytes);
            }
            return frameData;
        }




        public static void MaskBuffer(byte[] buffer, byte[] mask)
        {
            for (int i = 0; i< buffer.Length; i++)
            {
                buffer[i] ^= mask[i % mask.Length];
            }
        }

   }
    public class WebSocketFrame
    {
        byte[] _buffer;
        long _iCurrLocation = 0;
        long _lFrameHeaders = 0;
        public WebSocketFrame(long length, bool mask)
        {
            _lFrameHeaders = WebSocketUtils.GetFrameSize(length, mask);
            _buffer = new byte[_lFrameHeaders + length];
            AppendFramePrefix(length, mask);
        }

        public byte[] MaskKey { get; private set; } = null;

        public void AppendData(byte byteToAppend)
        {
            AppendData(new byte[] { byteToAppend });
        }

        public void AppendData(ArraySegment<byte> dataToAppend)
        {
            if (dataToAppend.Count > (_buffer.Length - _iCurrLocation)) throw new Exception("Frame size exceeded");
            if (MaskKey != null)
            {
                for(int i = 0; i< dataToAppend.Count; i++, _iCurrLocation++)
                {
                    _buffer[_iCurrLocation] = (byte)(dataToAppend[i] ^ MaskKey[(_iCurrLocation - _lFrameHeaders) % 4]);
                }
            }
            else
            {
                Array.Copy(dataToAppend.Array, dataToAppend.Offset, _buffer, _iCurrLocation, dataToAppend.Count);
                _iCurrLocation += dataToAppend.Count;
            }
        }

        internal void AppendFramePrefix(long dataLength, bool mask)
        {
            _buffer[_iCurrLocation++] = 0x82;  //Fin = 1; Opcode: Data - binary (2)
            byte maskBit = mask ? 0x80 : 0x00;

            if (dataLength <= 0x7D)
            {
                _buffer[_iCurrLocation++] = (byte)((byte)dataLength | maskBit);
            }
            else if (dataLength <= 0xFFFF)
            {
                _buffer[_iCurrLocation++] = (byte)(0x7E | maskBit);  //frame-payload-length-16
                byte[] len = BitConverter.GetBytes((UInt16)dataLength);
                Array.Copy(len, 0, _buffer, _iCurrLocation, len.Length);
                _iCurrLocation += len.Length;
            }
            else
            {
                _buffer[_iCurrLocation++] = (byte)(0x7F | maskBit);  //frame-payload-length-63
                byte[] len = BitConverter.GetBytes(dataLength);
                Array.Copy(len, 0, _buffer, _iCurrLocation, len.Length);
                _iCurrLocation += len.Length;
            }

            if (mask)
            {
                MaskKey = new byte[4];
                WebSocketUtils.GetRandomBytes(MaskKey);
                Array.Copy(MaskKey, 0, _buffer, _iCurrLocation, 4);
                _iCurrLocation += 4;
            }
        }

        public byte[] GetFrameBuffer()
        {
            if (_iCurrLocation < _buffer.Length) throw new Exception("Frame is not completed");
            return _buffer;
        }

    }
}