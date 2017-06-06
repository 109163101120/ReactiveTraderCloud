﻿using System;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using Adaptive.ReactiveTrader.EventStore.Domain;
using EventStore.ClientAPI;
using EventStore.ClientAPI.Exceptions;
using Newtonsoft.Json;
using Serilog;

namespace Adaptive.ReactiveTrader.EventStore.EventHandling
{
    public static class EventDispatcher
    {
        public static async Task<IDisposable> Start(IEventStoreConnection connection,
                                                    EventTypeResolver eventTypeResolver,
                                                    string streamName,
                                                    string groupName,
                                                    EventHandlerRouter eventHandlerRouter)
        {
            var subscription = await connection
                .ConnectToPersistentSubscriptionAsync(
                    stream: streamName,
                    groupName: groupName,
                    eventAppeared: async (s, e) => await OnEventAppeared(s, e, eventTypeResolver, eventHandlerRouter),
                    subscriptionDropped: OnSubscriptionDropped,
                    autoAck: false);

            // TODO - consider the blocking timeout here, and where the timeout should come from.
            return Disposable.Create(() => subscription.Stop(TimeSpan.FromSeconds(10)));
        }

        private static async Task OnEventAppeared(EventStorePersistentSubscriptionBase subscription,
                                                  ResolvedEvent resolvedEvent,
                                                  EventTypeResolver eventTypeResolver,
                                                  EventHandlerRouter eventHandlerRouter)
        {
            object payload;

            try
            {
                payload = DeserialiseEvent(resolvedEvent.Event, eventTypeResolver);
            }
            catch (Exception ex)
            {
                // Failed to deserialise. Park this event.
                subscription.Fail(resolvedEvent, PersistentSubscriptionNakEventAction.Park, $"Failed to deserialise. Error: {ex.Message}");
                return;
            }

            if (!eventHandlerRouter.CanRoute(payload))
            {
                // No event route handler found. Skip this event.
                subscription.Fail(resolvedEvent, PersistentSubscriptionNakEventAction.Skip, "Event handler route not found");
                return;
            }

            try
            {
                Log.Information("Dispatching event {event} from {eventNumber}@{streamId}",
                                resolvedEvent.Event.EventType,
                                resolvedEvent.OriginalEventNumber,
                                resolvedEvent.OriginalStreamId);

                var readEvent = ReadEvent.Create(resolvedEvent.OriginalStreamId,
                                                 resolvedEvent.Event.EventType,
                                                 resolvedEvent.OriginalEventNumber,
                                                 payload);

                await eventHandlerRouter.Route(readEvent);

                // All handlers successfully handled this event. Mark it as acknowledged.   
                subscription.Acknowledge(resolvedEvent);
            }
            catch (WrongExpectedVersionException ex)
            {
                // Concurrency error. Try again.
                Log.Warning("Concurrency error handling event {event} on stream {stream}. Retrying.",
                            resolvedEvent.Event.EventType,
                            resolvedEvent.OriginalStreamId);

                subscription.Fail(resolvedEvent, PersistentSubscriptionNakEventAction.Retry, ex.Message);
            }
            catch (Exception ex)
            {
                // A handler unexpectedly failed. Park this event.
                subscription.Fail(resolvedEvent, PersistentSubscriptionNakEventAction.Park, ex.Message);
            }
        }

        private static void OnSubscriptionDropped(EventStorePersistentSubscriptionBase subscription,
                                                  SubscriptionDropReason reason,
                                                  Exception exception)
        {
            // TODO - what to do here? Do we need any additional reconnect logic, or is the IConnected stuff enough?
            Log.Error(exception, "Connection to persisited subscription dropped. Reason: {reason}", reason);
        }

        private static object DeserialiseEvent(RecordedEvent evt, EventTypeResolver eventTypeResolver)
        {
            var targetType = eventTypeResolver.GetTypeForEventName(evt.EventType);
            var json = Encoding.UTF8.GetString(evt.Data);
            return JsonConvert.DeserializeObject(json, targetType);
        }
    }
}