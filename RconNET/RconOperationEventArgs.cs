using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace RconNET
{
    public enum RconOperationType
    {
        None,
        Connect,
        ExecuteCommand,
    }
    public enum RconOperationResult
    {
        None,
        Ok,
        ConnectFailed,
        AuthenticationFailed,
    }

    public class RconOperationEventArgs
    {
        private RconOperationType _operation = RconOperationType.None;
        private RconOperationResult _result = RconOperationResult.None;
        private string _resultString = string.Empty;

        //private int _operationId = 0;
        private ManualResetEventSlim _locker;

        public RconOperationType Operation
        {
            get { return _operation; }
            internal set { _operation = value; }
        }
        public RconOperationResult Result
        {
            get { return _result; }
            internal set { _result = value; }
        }
        public string ResultString
        {
            get { return _resultString; }
            internal set { _resultString = value; }
        }

        //internal int OperationId
        //{
        //    get { return _operationId; }
        //    set { _operationId = value; }
        //}
        internal ManualResetEventSlim Locker
        {
            get { return _locker; }
            set { _locker = value; }
        }    
    }

    public delegate void AsyncRconOperationDelegate(AsyncRconOperationEventArgs result);
    public class AsyncRconOperationEventArgs : RconOperationEventArgs
    {
        private object _state;

        private AsyncRconOperationDelegate _callback;

        public object State
        {
            get { return _state; }
            set { _state = value; }
        }

        internal AsyncRconOperationDelegate Callback
        {
            get { return _callback; }
            set { _callback = value; }
        }
    }
}
