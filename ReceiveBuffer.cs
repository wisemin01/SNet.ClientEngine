namespace SNet.ClientEngine
{
    using System;

    /// <summary>
    ///     bytes 수신에 필요한 리시브 버퍼 클래스.
    /// </summary>
    internal sealed class ReceiveBuffer
    {
        private readonly ArraySegment<byte> m_Buffer;
        private int m_ReadPosition;
        private int m_WritePosition;

        public int DataSize
        {
            get { return m_WritePosition - m_ReadPosition; }
        }

        public int FreeSize
        {
            get { return m_Buffer.Count - m_WritePosition; }
        }

        public ReceiveBuffer(int bufferSize)
        {
            m_Buffer = new ArraySegment<byte>(new byte[bufferSize], 0, bufferSize);
        }

        public ReceiveBuffer(byte[] source, int offset, int count)
        {
            m_Buffer = new ArraySegment<byte>(source, offset, count);
        }

        public ArraySegment<byte> ReadSegment
        {
            get { return new ArraySegment<byte>(m_Buffer.Array, m_Buffer.Offset + m_ReadPosition, DataSize); }
        }

        public ArraySegment<byte> WriteSegment
        {
            get { return new ArraySegment<byte>(m_Buffer.Array, m_Buffer.Offset + m_WritePosition, FreeSize); }
        }

        public void Clean()
        {
            int dataSize = DataSize;
            if (dataSize == 0)
            {
                m_ReadPosition = 0;
                m_WritePosition = 0;
            }
            else
            {
                Array.Copy(m_Buffer.Array, m_Buffer.Offset + m_ReadPosition, m_Buffer.Array, m_Buffer.Offset, dataSize);
                m_ReadPosition = 0;
                m_WritePosition = dataSize;
            }
        }

        public bool OnRead(int numOfBytes)
        {
            if (numOfBytes > DataSize)
                return false;

            m_ReadPosition += numOfBytes;
            return true;
        }

        public bool OnWrite(int numOfBytes)
        {
            if (numOfBytes > FreeSize)
                return false;

            m_WritePosition += numOfBytes;
            return true;
        }
    }
}
