using System;
using System.Collections.Generic;
using System.Text;

namespace SNet.ClientEngine
{
    using System;

    /// <summary>
    ///     클라이언트가 서버와 주고받을 메시지 객체를 생성하는데 도움을 주는 객체입니다.
    /// </summary>
    /// <typeparam name="TMessage">메시지 클래스 타입</typeparam>
    public interface IMessageProvider<TMessage>
    {
        /// <summary>
        ///     메시지의 헤더 사이즈를 반환합니다.
        /// </summary>
        /// <returns>헤더 사이즈</returns>
        int GetHeaderSize();

        /// <summary>
        ///     얻어낸 헤더 bytes 로부터 메시지의 길이를 가져옵니다.
        ///     (이때 반환값은 헤더 사이즈를 제외한 값입니다)
        /// </summary>
        /// <param name="header">메시지 헤더 bytes</param>
        /// <returns>메시지의 길이</returns>
        int GetBodyLengthFromHeader(ArraySegment<byte> header);

        /// <summary>
        ///     헤더와 메시지 bytes 로부터 메시지 객체를 생성합니다.
        /// </summary>
        /// <param name="header">메시지 헤더 bytes</param>
        /// <param name="body">메시지 bytes</param>
        /// <returns>생성된 메시지 객체</returns>
        TMessage ParseMessage(ArraySegment<byte> header, ArraySegment<byte> body);
    }
}
