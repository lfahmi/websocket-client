﻿using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Websocket.Client.Tests.TestServer
{
    // This is from https://github.com/aspnet/AspNetCore.Docs/blob/master/aspnetcore/fundamentals/websockets/samples/2.x/WebSocketsSample/Startup.cs
    public class SimpleStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {

        }

        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            app.UseWebSockets();
            app.Use(async (context, next) =>
            {
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        var webSocket = await context.WebSockets.AcceptWebSocketAsync();
                        await HandleRequest(webSocket, context);
                    }
                    else
                    {
                        context.Response.StatusCode = 400;
                    }
                }
                else
                {
                    await next();
                }
            });
        }

        protected virtual async Task HandleRequest(WebSocket webSocket, HttpContext context)
        {
            while(true)
            {
                var request = await ReadRequest(webSocket);
                if(!request.active)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "closed by client", CancellationToken.None);
                    return;
                }

                if (request.message.MessageType == WebSocketMessageType.Text)
                    await HandleTextRequest(webSocket, context, request.message);
            }
        }

        protected virtual Task HandleTextRequest(WebSocket webSocket, HttpContext context, ResponseMessage request)
        {
            var msg = (request.Text ?? string.Empty).Trim().ToLower();

            switch (msg)
            {
                case "ping":
                    return SendResponse(webSocket, ResponseMessage.TextMessage("pong"));
                case string echoMsg when msg.StartsWith("echo"):
                    return SendEcho(webSocket, echoMsg);
            }

            throw new NotSupportedException($"Request: '{msg}' is not supported");
        }

        protected virtual async Task<(bool active, ResponseMessage message)> ReadRequest(WebSocket webSocket)
        {
            var buffer = new ArraySegment<byte>(new byte[8192]);

            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await webSocket.ReceiveAsync(buffer, CancellationToken.None);
                    if(result.CloseStatus.HasValue)
                        return (false, null);

                    if (buffer.Array != null)
                        ms.Write(buffer.Array, buffer.Offset, result.Count);
                } while (!result.EndOfMessage);

                ms.Seek(0, SeekOrigin.Begin);

                ResponseMessage message;
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var data = GetEncoding().GetString(ms.ToArray());
                    message = ResponseMessage.TextMessage(data);
                }
                else
                {
                    var data = ms.ToArray();
                    message = ResponseMessage.BinaryMessage(data);
                }

                return (true, message);
            }
        }

        protected virtual async Task SendResponse(WebSocket webSocket, ResponseMessage message)
        {
            if(message.MessageType == WebSocketMessageType.Binary)
            {
                await webSocket.SendAsync(
                    new ArraySegment<byte>(message.Binary, 0, message.Binary.Length), 
                    message.MessageType, 
                    true, 
                    CancellationToken.None);
                return;
            }

            if (message.MessageType == WebSocketMessageType.Text)
            {
                var encoding = GetEncoding();
                var bytes = encoding.GetBytes(message.Text);
                await webSocket.SendAsync(
                    new ArraySegment<byte>(bytes, 0, bytes.Length),
                    message.MessageType,
                    true,
                    CancellationToken.None);
                return;
            }
        }

        protected virtual Encoding GetEncoding()
        {
            return Encoding.UTF8;
        }


        private async Task SendEcho(WebSocket webSocket, string msg)
        {
            await Task.Delay(100);
            await SendResponse(webSocket, ResponseMessage.TextMessage(msg));
        }
    }
}