﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LightestNight.System.EventSourcing.Checkpoints;
using LightestNight.System.EventSourcing.Observers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SqlStreamStore;
using SqlStreamStore.Streams;
using SqlStreamStore.Subscriptions;

namespace LightestNight.System.EventSourcing.SqlStreamStore.Subscriptions
{
    public class EventSubscription : BackgroundService
    {
        private static IAllStreamSubscription? _subscription;
        private static int _failureCount;

        private readonly ILogger<EventSubscription> _logger;
        private readonly IStreamStoreFactory _streamStoreFactory;
        private readonly GetGlobalCheckpoint _getGlobalCheckpoint;
        private readonly SetGlobalCheckpoint _setGlobalCheckpoint;

        public EventSubscription(ILogger<EventSubscription> logger, IStreamStoreFactory streamStoreFactory,
            SetGlobalCheckpoint setGlobalCheckpoint, GetGlobalCheckpoint getGlobalCheckpoint)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _streamStoreFactory = streamStoreFactory ?? throw new ArgumentNullException(nameof(streamStoreFactory));
            _setGlobalCheckpoint = setGlobalCheckpoint ?? throw new ArgumentNullException(nameof(setGlobalCheckpoint));
            _getGlobalCheckpoint = getGlobalCheckpoint ?? throw new ArgumentNullException(nameof(getGlobalCheckpoint));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation($"{nameof(EventSubscription)} is starting...");
            stoppingToken.Register(() => _logger.LogInformation($"{nameof(EventSubscription)} is stopping..."));

            if (!ObserverCollection.GetEventObservers().Any())
                // There are no observers currently registered in the system, save resources and do not set up a subscription yet
                _logger.LogDebug("No event observers registered at subscription initialisation time");
            else
                await SetSubscription(stoppingToken).ConfigureAwait(false);

            ObserverCollection.EventObserverRegistered += async args =>
            {
                await SetSubscription(stoppingToken).ConfigureAwait(false);
            };

            ObserverCollection.EventObserverUnregistered += async args =>
            {
                await SetSubscription(stoppingToken).ConfigureAwait(false);
            };
        }

        public override void Dispose()
        {
            _subscription?.Dispose();
            GC.SuppressFinalize(this);
            
            base.Dispose();
        }

        private async Task SetSubscription(CancellationToken cancellationToken = default)
        {
            var streamStoreTask = _streamStoreFactory.GetStreamStore(cancellationToken: cancellationToken);
            var checkpoint = await _getGlobalCheckpoint(cancellationToken).ConfigureAwait(false);

            _subscription?.Dispose();

            var streamStore = await streamStoreTask.ConfigureAwait(false); 
            _subscription = streamStore.SubscribeToAll(checkpoint, StreamMessageReceived, SubscriptionDropped);
            _logger.LogInformation($"The {Constants.GlobalCheckpointId} subscription has been created");
        }

        private async Task StreamMessageReceived(IAllStreamSubscription subscription, StreamMessage message,
            CancellationToken cancellationToken)
        {
            if (message.IsInSystemStream())
            {
                _logger.LogInformation(
                    $"Event {message.Type} is in a System stream therefore not being sent to observers");
                return;
            }

            var eventObservers = ObserverCollection.GetEventObservers().ToArray();
            _logger.LogInformation(
                $"Event {message.Type} received, sending to {eventObservers.Length} observer{(eventObservers.Length > 1 ? "s" : string.Empty)}");

            var eventSourceEvent = await message.ToEvent(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(eventObservers.Select(observer =>
                    observer.EventReceived(eventSourceEvent, cancellationToken)))
                .ConfigureAwait(false);

            await _setGlobalCheckpoint(subscription.LastPosition, cancellationToken).ConfigureAwait(false);
        }

        private void SubscriptionDropped(IAllStreamSubscription subscription, SubscriptionDroppedReason reason,
            Exception exception)
        {
            var streamStoreTask = _streamStoreFactory.GetStreamStore();
            _subscription?.Dispose();

            _failureCount++;
            if (_failureCount >= 5)
            {
                _logger.LogCritical(exception, $"Event Subscription dropped. Reason: {reason}");
            }
            else
            {
                _logger.LogError(exception,
                    $"Event Subscription dropped. Reason: {reason}. Failure #{_failureCount}. Attempting to reconnect...");

                var streamStore = streamStoreTask.Result;
                _subscription = streamStore.SubscribeToAll(subscription.LastPosition, StreamMessageReceived,
                    SubscriptionDropped);
            }
        }
    }
}