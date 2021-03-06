﻿using System;
using System.Threading.Tasks;
using System.Collections.Concurrent;

using Newtonsoft.Json;
using WebSocket4Net;

namespace DiscordWebRPC
{
    using TaskQueue = ConcurrentDictionary<Guid, TaskCompletionSource<object>>;

    public class DiscordSocketRPC
    {
        // private members
        private WebSocket _socket;
        private TaskQueue _taskQueue;
        private readonly JsonSerializerSettings _jsonSettings;

        // public events
        public event EventHandler<DispatchEvent> OnDispatch;

        public DiscordSocketRPC()
        {
            _jsonSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };

            _taskQueue = new TaskQueue();
            _socket = null;
        }

        public async Task<InviteEvent> SendInvite(string code)
        {
            if (!Connected)
                throw new InvalidOperationException();

            var task = new TaskCompletionSource<object>();
            var task_id = Guid.NewGuid();
            _taskQueue[task_id] = task;

            SendEvent(task_id, "INVITE_BROWSER", new InviteEvent
            {
                Code = code
            });

            return await task.Task.Timeout().ConfigureAwait(false) as InviteEvent;
        }

        public async Task<bool> Connect()
        {
            if (Connected)
                return true;

            _taskQueue.Clear();

            for(var i = DiscordRPC.RPC_STARTING_PORT; i > DiscordRPC.RPC_ENDING_PORT; i--)
            {
                _socket = new WebSocket($"ws://127.0.0.1:{i--}?v={DiscordRPC.RPC_VERSION}", origin: DiscordRPC.RPC_ORIGIN);
                _socket.MessageReceived += _socket_OnMessage;
                _socket.NoDelay = true;

                if (await _socket.OpenAsync().Timeout().ConfigureAwait(false))
                    return true;
            }

            return false;
        }


        private void _socket_OnMessage(object sender, MessageReceivedEventArgs e)
        {
            var obj = JsonConvert.DeserializeObject<RPCEvent>(e.Message);
            if (obj == null || obj.Nonce == null || !_taskQueue.TryGetValue(obj.Nonce, out var task))
            {
                if (obj.Cmd == "DISPATCH")
                    OnDispatch(this, obj.Data as DispatchEvent);
                
                return;
            }

            task.TrySetResult(obj.Data);
        }

        private void SendEvent<T>(Guid id, string name, T event_obj)
        {
            var obj = new
            {
                cmd = name,
                nonce = id.ToString(),
                args = event_obj as object
            };

            var serializedObj = JsonConvert.SerializeObject(obj, Formatting.None, _jsonSettings);
            _socket.Send(serializedObj);
        }

        private bool Connected => _socket != null && _socket.State == WebSocketState.Open;
    }
}
