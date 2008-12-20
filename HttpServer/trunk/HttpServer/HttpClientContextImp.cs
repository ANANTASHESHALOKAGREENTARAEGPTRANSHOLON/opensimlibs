using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Fadd;
using HttpServer.Exceptions;

namespace HttpServer
{
    /// <summary>
    /// Contains a connection to a browser/client.
    /// </summary>
    public class HttpClientContextImp : IHttpClientContext
    {
        /// <summary>
        /// Buffersize determines how large the HTTP header can be.
        /// </summary>
        public static int BufferSize = 16384;
        private readonly byte[] _buffer = new byte[BufferSize];
        private int _bytesLeft;
        private readonly ClientDisconnectedHandler _disconnectHandler;
        private readonly ILogWriter _log;
        private readonly HttpRequestParser _parser;
        private readonly RequestReceivedHandler _requestHandler;
        private readonly bool _secured;
        private readonly Stream _stream;
        private readonly string _remoteAddress;
        private readonly string _remotePort;
        private readonly Socket _sock;
    	
        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientContextImp"/> class.
        /// </summary>
        /// <param name="secured">true if the connection is secured (SSL/TLS)</param>
        /// <param name="remoteEndPoint">client that connected.</param>
        /// <param name="requestHandler">delegate handling incoming requests.</param>
        /// <param name="disconnectHandler">delegate being called when a client disconnectes</param>
        /// <param name="stream">Stream used for communication</param>
        /// <exception cref="SocketException">If beginreceive fails</exception>
        /// <param name="writer">delegate used to write log entries</param>
        /// <see cref="RequestReceivedHandler"/>
        /// <see cref="ClientDisconnectedHandler"/>
        public HttpClientContextImp(bool secured, IPEndPoint remoteEndPoint, 
                                    RequestReceivedHandler requestHandler,
                                   ClientDisconnectedHandler disconnectHandler,
                                   Stream stream, ILogWriter writer, Socket sock)
        {
            Check.Require(requestHandler, "requestHandler");
            Check.Require(remoteEndPoint, "remoteEndPoint");
            Check.NotEmpty(remoteEndPoint.Address.ToString(), "remoteEndPoint.Address");
            Check.Require(stream, "stream");
            Check.Require(sock, "socket");

            if (!stream.CanWrite || !stream.CanRead)
                throw new ArgumentException("Stream must be writeable and readable..");

			_remoteAddress = remoteEndPoint.Address.ToString();
			_remotePort = remoteEndPoint.Port.ToString();
            _log = writer ?? NullLogWriter.Instance;
            _parser = new HttpRequestParser(OnRequestCompleted, null);
            _secured = secured;
            _requestHandler = requestHandler;
            _disconnectHandler = disconnectHandler;
            _stream = stream;
            _sock = sock;
            try
            {
                _stream.BeginRead(_buffer, 0, BufferSize, OnReceive, null);
            }
            catch (IOException err)
            {
                try
                {
                    _sock.Shutdown(SocketShutdown.Both);
                    _sock.Close();
                } // TODO: FIXME: change this to a respectable exception after testing
                catch (System.AppDomainUnloadedException)
                {
                }
                _log.Write(this, LogPrio.Debug, err.ToString());
                _stream = null;
                _disconnectHandler(this, SocketError.ConnectionAborted);
            }
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="HttpClientContextImp"/> class.
        /// </summary>
        /// <param name="secured">true if the connection is secured (SSL/TLS)</param>
        /// <param name="requestHandler">delegate handling incoming requests.</param>
        /// <param name="remoteEndPoint">client that connected</param>
        /// <param name="stream">Stream used for communication</param>
        /// <exception cref="SocketException">If beginreceive fails</exception>
        /// <see cref="RequestReceivedHandler"/>
        /// <see cref="ClientDisconnectedHandler"/>
        public HttpClientContextImp(bool secured, IPEndPoint remoteEndPoint, Stream stream, RequestReceivedHandler requestHandler, Socket sock)
            : this(secured, remoteEndPoint, requestHandler, null, stream, null, sock)
        {

        }

        /// <summary>
        /// Using SSL or other encryption method.
        /// </summary>
        public bool Secured
        {
            get { return _secured; }
        }

        /// <summary>
        /// Disconnect from client
        /// </summary>
        /// <param name="error">error to report in the <see cref="ClientDisconnectedHandler"/> delegate.</param>
        public void Disconnect(SocketError error)
        {
            // disconnect may not throw any exceptions
            if (error == SocketError.Success)
            {
                _sock.Disconnect(true);
                //_sock.Shutdown(SocketShutdown.Both);
               // _sock.Close();
            }
            try
            {
                _stream.Close();
            }
            catch (Exception err)
            {
                _log.Write(this, LogPrio.Error, "Disconnect threw an exception: " + err);
            }

            if (_disconnectHandler != null)
                _disconnectHandler(this, error);
        }

        private void OnReceive(IAsyncResult ar)
        {
            
            try
            {
                int bytesRead = _stream.EndRead(ar);
                if (bytesRead == 0)
                {
                    Disconnect(SocketError.ConnectionReset);
                    return;
                }
                _bytesLeft += bytesRead;
                if (_bytesLeft > _buffer.Length)
                {
#if DEBUG
                    throw new BadRequestException("Too large HTTP header: " + Encoding.UTF8.GetString(_buffer, 0, bytesRead));
#else
                    throw new BadRequestException("Too large HTTP header: " + _bytesLeft);
#endif
                }

#if DEBUG
#pragma warning disable 219
                string temp = Encoding.ASCII.GetString(_buffer, 0, _bytesLeft);
                _log.Write(this, LogPrio.Trace, "Received: " + temp);
#pragma warning restore 219
#endif
                int offset = _parser.ParseMessage(_buffer, 0, _bytesLeft);
                Check100Continue();

                // try again to see if we can parse another message (check parser to see if it is looking for a new message)
                int oldOffset = offset;
                while (_parser.CurrentState == HttpRequestParser.State.FirstLine && offset != 0 && _bytesLeft - offset > 0)
                {
#if DEBUG
                    temp = Encoding.ASCII.GetString(_buffer, offset, _bytesLeft - offset);
                    _log.Write(this, LogPrio.Trace, "Processing: " + temp);
#endif
                    offset = _parser.ParseMessage(_buffer, offset, _bytesLeft - offset);
                    Check100Continue();
                }

                // need to be able to move prev bytes, so restore offset.
                if (offset == 0)
                    offset = oldOffset;

                // copy unused bytes to the beginning of the array
                if (offset > 0 && _bytesLeft != offset)
                {
                    int bytesToMove = _bytesLeft - offset;
                    for (int i = 0; i < bytesToMove; ++i)
                        _buffer[i] = _buffer[i + offset];
                }

                _bytesLeft -= offset;
                if (_stream.CanRead)
                    _stream.BeginRead(_buffer, _bytesLeft, _buffer.Length - _bytesLeft, OnReceive, null);
            }
            catch (BadRequestException err)
            {
                _log.Write(this, LogPrio.Warning, "Bad request, responding with it. Error: " + err);
                try
                {
                    Respond("HTTP/1.0", HttpStatusCode.BadRequest, err.Message);
                }
                catch(Exception err2)
                {
                    _log.Write(this, LogPrio.Fatal, "Failed to reply to a bad request. " + err2);
                }
                Disconnect(SocketError.NoRecovery);
            }
            catch (IOException err)
            {
                _log.Write(this, LogPrio.Debug, "Failed to end receive: " + err.Message);
                if (err.InnerException is SocketException)
                    Disconnect((SocketError) ((SocketException) err.InnerException).ErrorCode);
                else
                    Disconnect(SocketError.ConnectionReset);
            }
            catch (ObjectDisposedException err)
            {
                _log.Write(this, LogPrio.Debug, "Failed to end receive : " + err.Message);
                Disconnect(SocketError.NotSocket);
            }
        }

        /// <summary>
        /// This method checks the request if a responde to 100-continue should be sent.
        /// </summary>
        /// <remarks>
        /// 100 continue is a value in the Expect header.
        /// It's used to let the webserver determine if a request can be handled. The client
        /// waits for a reply before sending the body.
        /// </remarks>
        protected virtual void Check100Continue()
        {
            // 100continue fix
            if (_parser.CurrentRequest.ShouldReplyTo100Continue())
                Respond("HTTP/1.0", HttpStatusCode.Continue, "Please continue mate.");
        }

        private void OnRequestCompleted(IHttpRequest request)
        {
            request.AddHeader("remote_addr", _remoteAddress);
            request.AddHeader("remote_port", _remotePort);
            _requestHandler(this, request);
        }

        /// <summary>
        /// Send a response.
        /// </summary>
        /// <param name="httpVersion">Either HttpHelper.HTTP10 or HttpHelper.HTTP11</param>
        /// <param name="statusCode">http status code</param>
        /// <param name="reason">reason for the status code.</param>
        /// <param name="body">html body contents, can be null or empty.</param>
        /// <exception cref="ArgumentException">If httpVersion is invalid.</exception>
        public void Respond(string httpVersion, HttpStatusCode statusCode, string reason, string body)
        {
            if (string.IsNullOrEmpty(httpVersion) || !httpVersion.StartsWith("HTTP/1"))
                throw new ArgumentException("Invalid HTTP version");

            if (string.IsNullOrEmpty(reason))
                reason = statusCode.ToString();

            byte[] buffer;
            if (string.IsNullOrEmpty(body))
                buffer = Encoding.ASCII.GetBytes(httpVersion + " " + (int) statusCode + " " + reason + "\r\n\r\n");
            else
            {
                buffer =
                    Encoding.ASCII.GetBytes(
                        string.Format("{0} {1} {2}\r\nContent-Type: text/html\r\nContent-Length: {3}\r\n\r\n{4}",
                                      httpVersion, (int) statusCode, reason ?? statusCode.ToString(), body.Length, body));
            }

            Send(buffer);
        }

        /// <summary>
        /// Send a response.
        /// </summary>
        /// <param name="httpVersion">Either HttpHelper.HTTP10 or HttpHelper.HTTP11</param>
        /// <param name="statusCode">http status code</param>
        /// <param name="reason">reason for the status code.</param>
        public void Respond(string httpVersion, HttpStatusCode statusCode, string reason)
        {
            Respond(httpVersion, statusCode, reason, null);
        }

        /// <summary>
        /// Send a response.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public void Respond(string body)
        {
            if (body == null) 
                throw new ArgumentNullException("body");
            Respond("HTTP/1.1", HttpStatusCode.OK, HttpStatusCode.OK.ToString(), body);
        }

        /// <summary>
        /// send a whole buffer
        /// </summary>
        /// <param name="buffer">buffer to send</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Send(byte[] buffer)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            Send(buffer, 0, buffer.Length);
        }

        /// <summary>
        /// Send data using the stream
        /// </summary>
        /// <param name="buffer">Contains data to send</param>
        /// <param name="offset">Start position in buffer</param>
        /// <param name="size">number of bytes to send</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void Send(byte[] buffer, int offset, int size)
        {
            if (offset + size > buffer.Length)
                throw new ArgumentOutOfRangeException("offset", offset, "offset + size is beyond end of buffer.");

            if (_stream != null)
                _stream.Write(buffer, offset, size);
        }
    }


    /// <summary>
    /// Client have been disconnected.
    /// </summary>
    /// <param name="client">Client that was disconnected.</param>
    /// <param name="error">Reason</param>
    /// <see cref="IHttpClientContext"/>
    public delegate void ClientDisconnectedHandler(IHttpClientContext client, SocketError error);

    /// <summary>
    /// Invoked when a client context have received a new HTTP request
    /// </summary>
    /// <param name="client">Client that received the request.</param>
    /// <param name="request">Request that was received.</param>
    /// <see cref="IHttpClientContext"/>
    public delegate void RequestReceivedHandler(IHttpClientContext client, IHttpRequest request);
}