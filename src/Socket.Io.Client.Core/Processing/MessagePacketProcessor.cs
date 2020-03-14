﻿using System;
using System.Collections.Generic;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Socket.Io.Client.Core.Model.SocketEvent;
using Socket.Io.Client.Core.Model.SocketIo;
using Utf8Json;

namespace Socket.Io.Client.Core.Processing
{
    internal class MessagePacketProcessor : IPacketProcessor
    {
        private readonly ISocketIoClient _client;
        private readonly ILogger _logger;

        internal MessagePacketProcessor(ISocketIoClient client, ILogger logger)
        {
            _client = client;
            _logger = logger;
        }

        public void Process(Packet packet)
        {
            if (_logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug($"Processing message packet: {packet}");

            switch (packet.SocketIoType)
            {
                case SocketIoType.Event:
                case SocketIoType.Ack:
                    ParseAndEmitEvents(packet);
                    break;
                case SocketIoType.Connect:
                    //40 packet
                    _client.Events.ConnectSubject.OnNext(Unit.Default);
                    break;
                case SocketIoType.Disconnect:
                case SocketIoType.Error:
                    throw new NotImplementedException();
                case SocketIoType.BinaryEvent:
                case SocketIoType.BinaryAck:
                    throw new NotSupportedException();
                case null:
                    _logger.LogWarning($"Cannot handle message packet without SocketIo type. Packet: {packet}");
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ParseAndEmitEvents(Packet packet)
        {
            try
            {
                var eventArray = JsonSerializer.Deserialize<string[]>(packet.Data);
                if (eventArray != null && eventArray.Length > 0)
                {
                    if (packet.Id.HasValue && _logger.IsEnabled(LogLevel.Debug))
                        _logger.LogDebug($"Received packet with ACK: {packet.Id.Value}");

                    if (packet.SocketIoType == SocketIoType.Ack && packet.Id.HasValue)
                    {
                        _client.Events.AckMessageSubject.OnNext(new AckMessageEvent(packet.Id.Value, eventArray));
                    }
                    else
                    {
                        //first element should contain event name
                        //we can have zero, one or multiple arguments after event name so emit based on number of them
                        var message = eventArray.Length == 1
                            ? new EventMessageEvent(eventArray[0], new List<string>())
                            : new EventMessageEvent(eventArray[0], eventArray[1..]);
                        _client.Events.EventMessageSubject.OnNext(message);
                    }
                }
            }
            catch (JsonParsingException ex)
            {
                _logger.LogError(ex, $"Error while deserializing event message. Packet: {packet}");
                _client.Events.ErrorSubject.OnNext(new ErrorEvent(ex, "Error while deserializing event message"));
            }
        }
    }
}