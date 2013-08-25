using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RconNET
{
    internal class ProtocolPacketArrivedEventArgs : EventArgs
    {
        private byte[] _packet;

        public byte[] Packet
        {
            get
            {
                return _packet;
            }
        }

        public ProtocolPacketArrivedEventArgs()
            : this(new byte[0])
        {
        }
        public ProtocolPacketArrivedEventArgs(byte[] data)
        {
            _packet = data;
        }
    }

    internal class PacketProtocol
    {
        private byte[] _lengthBuffer = new byte[sizeof(int)];
        private byte[] _dataBuffer = null;
        private int _bytesReceived = 0;
        private int _maxPacketSize = 1048576; // default 1 MB

        internal int MaxPacketSize
        {
            get { return _maxPacketSize; }
            set { _maxPacketSize = value; }
        }

        internal event EventHandler<ProtocolPacketArrivedEventArgs> MessageArrived;

        internal void DataReceived(byte[] data)
        {
            DataReceived(data, 0, data.Length);
        }
        internal void DataReceived(byte[] buffer, int offset, int length)
        {
            int offsetLength = offset + length;
            while (offset < offsetLength)
            {
                int available = offsetLength - offset;
                if (_dataBuffer != null)
                {
                    int transferred = Math.Min(/*how many bytes to complete msg*/_dataBuffer.Length - _bytesReceived, available);//count how many bytes we can read
                    //Array.Copy(buffer, offset, _dataBuffer, _bytesReceived, transferred);
                    Buffer.BlockCopy(buffer, offset, _dataBuffer, _bytesReceived, transferred);
                    offset += transferred;
                    ReadFinished(transferred);
                }
                else
                {
                    int transferred = Math.Min(_lengthBuffer.Length - _bytesReceived, available);
                    //Array.Copy(buffer, offset, _lengthBuffer, _bytesReceived, transferred);
                    Buffer.BlockCopy(buffer, offset, _lengthBuffer, _bytesReceived, transferred);
                    offset += transferred;
                    ReadFinished(transferred);
                }
            }
        }
        private void ReadFinished(int bytesCount)
        {
            _bytesReceived += bytesCount;
            if (_dataBuffer != null)
            {
                if (_bytesReceived >= _dataBuffer.Length)
                {
                    if (MessageArrived != null) MessageArrived(this, new ProtocolPacketArrivedEventArgs(_dataBuffer));
                    _dataBuffer = null;
                    _bytesReceived = 0;
                }
            }
            else
            {
                if (_bytesReceived >= sizeof(int))
                {
                    int size = BitConverter.ToInt32(_lengthBuffer, 0);
                    if (size <= _maxPacketSize) _dataBuffer = new byte[size];
                    _bytesReceived = 0;
                }
            }
        }

        internal static byte[] CreateMessage(byte[] message)
        {
            byte[] lPref = BitConverter.GetBytes(message.Length);
            byte[] result = new byte[lPref.Length + message.Length];
            //Array.Copy(lPref, result, lPref.Length);
            Buffer.BlockCopy(lPref, 0, result, 0, lPref.Length);
            //Array.Copy(message, 0, result, lPref.Length, message.Length);
            Buffer.BlockCopy(message, 0, result, lPref.Length, message.Length);
            return result;
        }

        internal void Reset()
        {
            _dataBuffer = null;
            _bytesReceived = 0;
        }
    }
}
