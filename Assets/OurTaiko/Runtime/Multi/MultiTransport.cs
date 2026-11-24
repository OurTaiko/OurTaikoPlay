using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Net.Http;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using OurTaiko.Multi.Proto;

namespace OurTaiko.Multi
{
    // Something the player should be told about a room or its connection. Code is the server's
    // (ROOM_FULL, KICKED, …) or this client's own; MultiText.Describe gives its text.
    public sealed class MultiException : Exception
    {
        public MultiException(string code, Exception inner = null) : base(code, inner) { Code = code; }
        public string Code { get; }
    }

    // One open stream to the room server: messages out, messages in.
    public interface IMultiLink : IDisposable
    {
        Task SendAsync(ClientMessage message);
        // The next message; null once the server has ended the stream.
        Task<ServerMessage> ReceiveAsync(CancellationToken cancel);
    }

    // The gRPC side of マルチ演奏: the channel to OurTaikoNet's room server and its one bidirectional
    // call, ourtaiko.multiplay.v1.MultiEnso/Connect (the proto is OurTaikoNet's
    // proto/ourtaiko/multiplay/v1/multiplay.proto; Proto/Multiplay.cs is generated from it and there
    // is no generated client, the method is declared here). gRPC needs HTTP/2, which Unity's own
    // HttpClient (and so OurTaikoHttpClient) does not speak, so the handler is YetAnotherHttpHandler
    // (native, every platform the game builds for), under the game's User-Agent.
    public static class MultiTransport
    {
        // Opens a stream to the server at an address, signed in with a game session. Tests put a
        // room server of their own here.
        public static Func<string, string, CancellationToken, IMultiLink> Open { get; set; } = OpenGrpc;

        static readonly Marshaller<ClientMessage> ClientMarshaller =
            Marshallers.Create(message => message.ToByteArray(), ClientMessage.Parser.ParseFrom);
        static readonly Marshaller<ServerMessage> ServerMarshaller =
            Marshallers.Create(message => message.ToByteArray(), ServerMessage.Parser.ParseFrom);
        static readonly Method<ClientMessage, ServerMessage> ConnectMethod = new Method<ClientMessage, ServerMessage>(
            MethodType.DuplexStreaming, "ourtaiko.multiplay.v1.MultiEnso", "Connect", ClientMarshaller, ServerMarshaller);

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Open = OpenGrpc;

        public static IMultiLink OpenGrpc(string address, string session, CancellationToken cancel)
        {
            var uri = new Uri(address);
            var handler = new YetAnotherHttpHandler
            {
                // Plain http:// (a local server) is HTTP/2 without TLS; https:// negotiates h2 by ALPN.
                Http2Only = uri.Scheme == Uri.UriSchemeHttp,
                Http2KeepAliveInterval = TimeSpan.FromSeconds(20),
                Http2KeepAliveTimeout = TimeSpan.FromSeconds(10),
                Http2KeepAliveWhileIdle = true,
            };
            var channel = GrpcChannel.ForAddress(uri, new GrpcChannelOptions
            {
                HttpHandler = new Online.OurTaikoHttpClient.UserAgentHandler(handler),
                DisposeHttpClient = true,
                MaxReceiveMessageSize = 1024 * 1024,
                MaxSendMessageSize = 64 * 1024,
            });
            var headers = new Metadata { { "authorization", "Bearer " + session } };
            var call = channel.CreateCallInvoker().AsyncDuplexStreamingCall(ConnectMethod, null, new CallOptions(headers, cancellationToken: cancel));
            return new GrpcLink(channel, call);
        }

        sealed class GrpcLink : IMultiLink
        {
            readonly GrpcChannel channel;
            readonly AsyncDuplexStreamingCall<ClientMessage, ServerMessage> call;

            public GrpcLink(GrpcChannel channel, AsyncDuplexStreamingCall<ClientMessage, ServerMessage> call)
            {
                this.channel = channel; this.call = call;
            }

            public async Task SendAsync(ClientMessage message)
            {
                try { await call.RequestStream.WriteAsync(message); }
                catch (RpcException error) { throw Translate(error); }
            }

            public async Task<ServerMessage> ReceiveAsync(CancellationToken cancel)
            {
                try { return await call.ResponseStream.MoveNext(cancel) ? call.ResponseStream.Current : null; }
                catch (RpcException error) when (error.StatusCode == StatusCode.Cancelled && cancel.IsCancellationRequested) { throw new OperationCanceledException(cancel); }
                catch (RpcException error) { throw Translate(error); }
            }

            public void Dispose()
            {
                try { call.Dispose(); } catch (Exception error) { Log.Debug("Multi", "Closing the call: " + error.Message); }
                try { channel.Dispose(); } catch (Exception error) { Log.Debug("Multi", "Closing the channel: " + error.Message); }
            }

            // The server's status codes that mean something to the player; the rest is the connection.
            static Exception Translate(RpcException error)
            {
                switch (error.StatusCode)
                {
                    case StatusCode.Unauthenticated: return new MultiException("AUTH_REQUIRED", error);
                    case StatusCode.PermissionDenied: return new MultiException("BANNED", error);
                    case StatusCode.ResourceExhausted: return new MultiException("TOO_MANY_MESSAGES", error);
                    case StatusCode.Unimplemented: return new MultiException("NOT_AVAILABLE", error);
                    case StatusCode.Unavailable when error.Status.Detail == "AUTH_UNAVAILABLE": return new MultiException("AUTH_UNAVAILABLE", error);
                    default: return new MultiException("CONNECTION_LOST", error);
                }
            }
        }
    }
}
