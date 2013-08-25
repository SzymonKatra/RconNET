using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Net;
using System.Net.Sockets;
using System.IO;

namespace RconNET
{
    public class RconClient
    {
        #region Const
        private const int SERVERDATA_AUTH = 3;
        private const int SERVERDATA_AUTH_RESPONSE = 2;
        private const int SERVERDATA_EXECCOMMAND = 2;
        private const int SERVERDATA_RESPONSE_VALUE = 0;
        #endregion

        #region Properties
        private Socket _socket;
        private PacketProtocol _protocol;
        private int _operationId= 1;
        private object _operationIdLock = new object();
        private bool _authenticated = false;
        private Dictionary<int, RconOperationEventArgs> _currentOperations;
        private object _currentOperationsLock = new object();
        private string _rconPassword = string.Empty;
        private int _connectOperationId = 0;
        private byte[] _buffer = new byte[2048];

        public bool Authenticated
        {
            get { return _authenticated; }
        }
        #endregion

        #region Events
        public event EventHandler Disconnected;
        #endregion

        #region Methods
        public RconOperationEventArgs Connect(IPEndPoint ipEndPoint, string rconPassword)
        {
            ManualResetEventSlim locker = new ManualResetEventSlim(false);
            RconOperationEventArgs result = StartConnect(ipEndPoint, rconPassword, locker, null, null);
            locker.Wait();
            locker.Dispose();
            return result;
        }
        public void ConnectAsync(IPEndPoint ipEndPoint, string rconPassword, AsyncRconOperationDelegate callback, object state = null)
        {
            StartConnect(ipEndPoint, rconPassword, null, callback, state);
        }
        public void Disconnect()
        {
            StartDisconnect();
        }

        public RconOperationEventArgs ExecuteCommand(string command)
        {
            ManualResetEventSlim locker = new ManualResetEventSlim(false);
            RconOperationEventArgs result = StartExecuteCommand(command, locker, null, null);
            locker.Wait();
            locker.Dispose();
            return result;
        }
        public void ExecuteCommandAsync(string command, AsyncRconOperationDelegate callback, object state = null)
        {
            StartExecuteCommand(command, null, callback, state);
        }

        private RconOperationEventArgs StartExecuteCommand(string command, ManualResetEventSlim locker, AsyncRconOperationDelegate callback, object state)
        {
            RconOperationEventArgs evArgs = InitResultObject(locker, callback, state);
            evArgs.Operation = RconOperationType.ExecuteCommand;

            int id = GetOperationId();
            lock (_currentOperationsLock) _currentOperations.Add(id, evArgs);

            using (PacketWriter writer = new PacketWriter())
            {
                writer.Write(id);
                writer.Write(SERVERDATA_EXECCOMMAND);
                writer.Write(Encoding.ASCII.GetBytes(command));
                writer.Write(0); //null terminated string
                writer.Write(0); //empty

                SendPacket(writer.Data);
            }

            return evArgs;
        }

        private RconOperationEventArgs StartConnect(IPEndPoint ipEndPoint, string rconPassword, ManualResetEventSlim locker, AsyncRconOperationDelegate callback, object state)
        {
            if (_socket != null && _socket.Connected) throw new InvalidOperationException("You are already connected! Please disconnect first");
            _currentOperations = new Dictionary<int, RconOperationEventArgs>();

            RconOperationEventArgs evArgs = InitResultObject(locker, callback, state);
            evArgs.Operation = RconOperationType.Connect;

            _connectOperationId = GetOperationId();
            lock (_currentOperationsLock) _currentOperations.Add(_connectOperationId, evArgs);

            _rconPassword = rconPassword;
            _socket = new Socket(ipEndPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            _socket.BeginConnect(ipEndPoint, ConnectCallback, null);

            return evArgs;
        }
        private void ConnectCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndConnect(ar);

                _protocol = new PacketProtocol();
                _protocol.MessageArrived += _protocol_MessageArrived;
                StartReceive();

                using (PacketWriter writer = new PacketWriter())
                {
                    writer.Write(_connectOperationId);
                    writer.Write(SERVERDATA_AUTH);
                    writer.Write(Encoding.ASCII.GetBytes(_rconPassword));
                    writer.Write(0); //null terminated string
                    writer.Write(0); //empty

                    SendPacket(writer.Data);
                }
            }
            catch
            {
                RconOperationEventArgs result;
                lock(_currentOperationsLock) result = _currentOperations[_connectOperationId];
                result.Result = RconOperationResult.ConnectFailed;
                FinalizeResultObject(_connectOperationId);
            }
        }

        private void StartReceive()
        {
            if (_socket == null) return;
            _socket.BeginReceive(_buffer, 0, _buffer.Length, SocketFlags.None, ReceiveCallback, null);
        }
        private void ReceiveCallback(IAsyncResult ar)
        {
            try
            {
                if (_socket == null) throw new SocketException();
                int len = _socket.EndReceive(ar);
                _protocol.DataReceived(_buffer, 0, len);
                StartReceive();
            }
            catch (SocketException)
            {
                FinalizeConnection();
            }
        }
        private void _protocol_MessageArrived(object sender, ProtocolPacketArrivedEventArgs e)
        {
            using (PacketReader reader = new PacketReader(e.Packet))
            {
                int id = reader.ReadInt32();
                int type = reader.ReadInt32();
                bool authFail = false;
                if (id == -1)//auth failed
                {
                    _authenticated = false;
                    id = _connectOperationId;
                    authFail = true;
                }
                if (type == SERVERDATA_RESPONSE_VALUE && id == _connectOperationId) //auth
                {
                    _authenticated = true;
                    return;
                }

                List<byte> bodyList = new List<byte>();
                byte lastByte = 0;
                do
                {
                    lastByte = reader.ReadByte();
                    bodyList.Add(lastByte);
                }
                while (lastByte != 0);
                bodyList.RemoveAt(bodyList.Count - 1);
                string body = Encoding.ASCII.GetString(bodyList.ToArray());

                RconOperationEventArgs result;
                lock (_currentOperationsLock)
                {
                    if (!_currentOperations.ContainsKey(id)) return;
                    result = _currentOperations[id];
                }

                result.Result = (authFail ? RconOperationResult.AuthenticationFailed : RconOperationResult.Ok);
                result.ResultString = body;

                FinalizeResultObject(id);

                if (authFail) StartDisconnect();
            }
        }

        private void StartDisconnect()
        {
            _socket.BeginDisconnect(false, DisconnectCallback, null);
        }
        private void DisconnectCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndDisconnect(ar);
            }
            finally { FinalizeConnection(); }
        }

        protected void FinalizeConnection()
        {
            _authenticated = false;

            if (_socket != null)
            {
                _socket.Shutdown(SocketShutdown.Both);
                _socket.Dispose();
                _socket = null;

                OnDisconnected();
            }
        }
        protected void SendPacket(byte[] packet)
        {
            byte[] fullPacket = PacketProtocol.CreateMessage(packet);
            _socket.BeginSend(fullPacket, 0, fullPacket.Length, SocketFlags.None, SendPacketCallback, null);
        }
        private void SendPacketCallback(IAsyncResult ar)
        {
            try
            {
                _socket.EndSend(ar);
            }
            catch { }
        }

        private RconOperationEventArgs InitResultObject(ManualResetEventSlim locker, AsyncRconOperationDelegate callback, object state)
        {
            RconOperationEventArgs result;

            if (locker == null) //async
            {
                AsyncRconOperationEventArgs asyncResult = new AsyncRconOperationEventArgs();
                asyncResult.Callback = callback;
                asyncResult.State = state;
                result = asyncResult;
            }
            else //sync
            {
                result = new RconOperationEventArgs();
                result.Locker = locker;
            }

            //result.OperationId = GetOperationId();

            return result;
        }
        private void FinalizeResultObject(int operationId)
        {
            RconOperationEventArgs result;
            lock (_currentOperationsLock)
            {
                result = _currentOperations[operationId];
                _currentOperations.Remove(operationId);
            }

            if (result is AsyncRconOperationEventArgs) //async
            {
                AsyncRconOperationEventArgs asyncResult = (AsyncRconOperationEventArgs)result;
                if (asyncResult.Callback != null) asyncResult.Callback(asyncResult);
            }
            else //sync
            {
                result.Locker.Set();
            }
        }

        protected int GetOperationId()
        {
            int ret;
            lock (_operationIdLock)
            {
                if (_operationId <= 0) _operationId = 1;
                while (_currentOperations.ContainsKey(_operationId) || _operationId == _connectOperationId) _operationId++;
                ret = _operationId++;
            }
            return ret;
        }

        protected virtual void OnDisconnected()
        {

            if (Disconnected != null) Disconnected(this, EventArgs.Empty);
        }
        #endregion
    }
}
