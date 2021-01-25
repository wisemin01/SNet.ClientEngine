namespace SNet.ClientEngine
{
    using System;
    using System.Collections.Generic;
    using System.Net;
    using System.Net.Sockets;
    using System.Threading;

    /// <summary>
    ///     클라이언트 클래스. 상속하여 사용한다
    /// </summary>
    /// <typeparam name="TMessage">메시지 클래스 타입</typeparam>
    public abstract class Client<TMessage>
    {
        private class StateType
        {
            public const int CLOSED = 0;
            public const int OPENED = 1;
        }

        private int m_State;

        private IMessageProvider<TMessage> m_MessageProvider;
        private readonly int m_HeaderSize;

        private readonly SocketAsyncEventArgs m_RecvArgs;
        private readonly SocketAsyncEventArgs m_SendArgs;

        private readonly ReceiveBuffer m_RecvBuffer;

        private readonly Queue<ArraySegment<byte>> m_SendQueue;
        private readonly List<ArraySegment<byte>> m_PendingList;
        private readonly object m_SendLock;

        private Socket m_Socket;

        public bool Connected
        {
            get { return m_Socket != null && m_Socket.Connected; }
        }

        public Client(IMessageProvider<TMessage> messageProvider, int recvBufferSize = 4096)
        {
            m_MessageProvider = messageProvider;
            m_HeaderSize = m_MessageProvider.GetHeaderSize();

            m_RecvArgs = new SocketAsyncEventArgs();
            m_SendArgs = new SocketAsyncEventArgs();
            m_RecvBuffer = new ReceiveBuffer(recvBufferSize);
            m_State = StateType.CLOSED;

            m_SendQueue = new Queue<ArraySegment<byte>>();
            m_PendingList = new List<ArraySegment<byte>>();
            m_SendLock = new object();

            m_RecvArgs.Completed += HandleReceiveCompleted;
            m_SendArgs.Completed += HandleSendCompleted;
        }

        /// <summary>
        ///     해당 <paramref name="endPoint"/>에 연결을 시도합니다.
        /// </summary>
        /// <param name="endPoint">연결 지점</param>
        public void Connect(EndPoint endPoint)
        {
            Socket connectSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp);

            SocketAsyncEventArgs args = new SocketAsyncEventArgs();
            args.Completed += HandleConnect;
            args.RemoteEndPoint = endPoint;
            args.UserToken = connectSocket;

            if (!connectSocket.ConnectAsync(args))
            {
                HandleConnect(null, args);
            }
        }

        /// <summary>
        ///     소켓 연결을 끊습니다.
        /// </summary>
        public void Disconnect()
        {
            if (Interlocked.Exchange(ref m_State, StateType.CLOSED) == StateType.CLOSED)
                return;

            OnDisconnected();

            m_Socket.Shutdown(SocketShutdown.Both);
            m_Socket.Close();
            m_Socket = null;
        }

        private void HandleConnect(object sender, SocketAsyncEventArgs args)
        {
            if (args.SocketError == SocketError.Success)
            {
                Interlocked.Exchange(ref m_State, StateType.OPENED);
                m_Socket = args.ConnectSocket;
                OnConnected();
                StartReceive();
            }
            else
            {
                Interlocked.Exchange(ref m_State, StateType.CLOSED);
                OnConnectFailed();
            }
        }

        #region Receive

        // 리시브 비동기 이벤트 등록
        private void StartReceive()
        {
            m_RecvBuffer.Clean();

            ArraySegment<byte> writeSegm = m_RecvBuffer.WriteSegment;
            m_RecvArgs.SetBuffer(writeSegm.Array, writeSegm.Offset, writeSegm.Count);

            if (m_State == StateType.CLOSED)
            {
                return;
            }

            if (!m_Socket.ReceiveAsync(m_RecvArgs))
            {
                HandleReceiveCompleted(null, m_RecvArgs);
            }
        }

        // 리시브 비동기 이벤트 콜백
        private void HandleReceiveCompleted(object sender, SocketAsyncEventArgs args)
        {
            if (m_RecvArgs.BytesTransferred > 0 &&
                args.SocketError == SocketError.Success)
            {
                try
                {
                    // 바이트 읽기 버퍼 에러
                    if (m_RecvBuffer.OnWrite(args.BytesTransferred) == false)
                    {
                        Disconnect();
                        return;
                    }

                    // 바이트 영역에서 패킷 생성 및 상위 레이어에 이벤트로 전달
                    if (!ParseMessageAndProcess())
                    {
                        Disconnect();
                        return;
                    }

                    // 리시브 재등록
                    StartReceive();
                }
                catch (Exception e)
                {
                    OnException(e);
                }
            }
            else
            {
                Disconnect();
            }
        }

        // 바이트 배열 -> 패킷 -> 이벤트레이어 전달 함수
        private bool ParseMessageAndProcess()
        {
            ArraySegment<byte> buffer = m_RecvBuffer.ReadSegment; // 남은 Bytes

            while (true)
            {
                // 헤더를 읽을 수 있는지 검사
                if (buffer.Count < m_HeaderSize)
                    break;

                // 헤더 바이트 추출
                ArraySegment<byte> headerBytes = buffer.Slice(0, m_HeaderSize);

                // 필요한 바디 사이즈 계산 + 남은 바이트가 충분한지 검사
                int contentLength = buffer.Count - m_HeaderSize;
                int bodyLength = m_MessageProvider.GetBodyLengthFromHeader(headerBytes);

                // 남은 바이트 수보다 헤더에 기입된 바이트 수가 많다면,
                // 아직 모든 바이트가 수신되지 않은 상태.
                if (bodyLength > contentLength)
                    break;

                // buffer는 아래와 같을 것이다.
                // 헤더를 제외한 바디 부분만의 Bytes를 추출한다.
                // [ HEADER(4 Bytes) ] [ BODY(N Bytes) ] [ UNKNOWN AREA(N Bytes) ]
                ArraySegment<byte> bodyBytes = buffer.Slice(m_HeaderSize, bodyLength);

                int processLength = ProcessMessageInternal(headerBytes, bodyBytes);
                if (processLength < 0)
                {
                    // 음수: 오류
                    return false;
                }
                buffer = buffer.Slice(processLength);
            }

            return true;
        }

        private int ProcessMessageInternal(ArraySegment<byte> headerBytes, ArraySegment<byte> bodyBytes)
        {
            // 패킷 생성 및 핸들러에 전송
            OnMessage(m_MessageProvider.ParseMessage(headerBytes, bodyBytes));

            // 처리한 양 계산
            int numOfProcessedBytes = headerBytes.Count + bodyBytes.Count;
            if (!m_RecvBuffer.OnRead(numOfProcessedBytes))
            {
                // 버퍼 오류
                return -1;
            }

            return numOfProcessedBytes;
        }

        #endregion Receive

        #region Send

        // 샌드 비동기 이벤트 등록
        private void StartSend()
        {
            try
            {
                // 팬딩 큐로 이동
                while (m_SendQueue.Count > 0)
                {
                    m_PendingList.Add(m_SendQueue.Dequeue());
                }
                m_SendArgs.BufferList = m_PendingList;

                if (m_State == StateType.CLOSED)
                {
                    return;
                }

                if (!m_Socket.SendAsync(m_SendArgs))
                {
                    HandleSendCompleted(null, m_SendArgs);
                }
            }
            catch (Exception e)
            {
                OnException(e);
                Disconnect();
            }
        }

        /// <summary>
        ///     byte[]를 소켓을 통해 전송합니다.
        /// </summary>
        /// <param name="bytes">보낼 bytes</param>
        /// <param name="offset">배열 offset</param>
        /// <param name="length">보낼 길이</param>
        public void Send(byte[] bytes, int offset, int length)
        {
            Send(new ArraySegment<byte>(bytes, offset, length));
        }

        /// <summary>
        ///     byte[]를 소켓을 통해 전송합니다.
        /// </summary>
        /// <param name="bytes">보낼 bytes</param>
        public void Send(ArraySegment<byte> bytes)
        {
            lock (m_SendLock)
            {
                m_SendQueue.Enqueue(bytes);
                if (m_PendingList.Count == 0)
                {
                    StartSend();
                }
            }
        }

        private void HandleSendCompleted(object sender, SocketAsyncEventArgs args)
        {
            lock (m_SendLock)
            {
                if (args.BytesTransferred > 0 && args.SocketError == SocketError.Success)
                {
                    try
                    {
                        m_SendArgs.BufferList = null;
                        m_PendingList.Clear();

                        if (m_SendQueue.Count > 0)
                        {
                            StartSend();
                        }
                    }
                    catch (Exception e)
                    {
                        OnException(e);
                    }
                }
                else
                {
                    Disconnect();
                }
            }
        }

        #endregion Send

        #region Event Handling

        /// <summary>
        ///     소켓 연결에 성공했을 때 호출됩니다.
        /// </summary>
        public virtual void OnConnected() { }
        /// <summary>
        ///     소켓 연결에 실패했을 때 호출됩니다.
        /// </summary>
        public virtual void OnConnectFailed() { }
        /// <summary>
        ///     소켓 연결이 끊어졌을 때 호출됩니다.
        /// </summary>
        public virtual void OnDisconnected() { }
        /// <summary>
        ///     서버로부터 메시지를 수신했을 때 호출됩니다.
        /// </summary>
        /// <param name="message">받은 메시지 객체</param>
        public virtual void OnMessage(TMessage message) { }
        /// <summary>
        ///     클라이언트 객체 내에서 예외가 발생했을 때 호출됩니다.
        /// </summary>
        /// <param name="e">예외 객체</param>
        public virtual void OnException(Exception e) { }

        #endregion
    }
}
