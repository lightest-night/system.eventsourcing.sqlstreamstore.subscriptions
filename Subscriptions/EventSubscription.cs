using System;
using System.Collections.Generic;
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

        private readonly IEventObserver[] _eventObservers;

        private readonly ILogger<EventSubscription> _logger;

        private readonly IStreamStoreFactory _streamStoreFactory;

        private readonly GetGlobalCheckpoint _getGlobalCheckpoint;

        private readonly SetGlobalCheckpoint _setGlobalCheckpoint;

        public EventSubscription(IEnumerable<IEventObserver> eventObservers, ILogger<EventSubscription> logger,
            IStreamStoreFactory streamStoreFactory, SetGlobalCheckpoint setGlobalCheckpoint,
            GetGlobalCheckpoint getGlobalCheckpoint)
        {
            _eventObservers = eventObservers.ToArray();
            _logger = logger;
            _streamStoreFactory = streamStoreFactory;
            _setGlobalCheckpoint = setGlobalCheckpoint;
            _getGlobalCheckpoint = getGlobalCheckpoint;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Initialising {observerCount} observers", _eventObservers.Length);
            await Task.WhenAll(_eventObservers.Select(observer => observer.InitialiseObserver(stoppingToken)))
                .ConfigureAwait(false);

            _logger.LogInformation("{subscriptionName} is starting...", nameof(EventSubscription));
            stoppingToken.Register(() =>
                _logger.LogInformation("{subscriptionName} is stopping...", nameof(EventSubscription)));

            await SetSubscription(cancellationToken: stoppingToken).ConfigureAwait(false);
        }

        public override void Dispose()
        {
            _subscription?.Dispose();
            GC.SuppressFinalize(this);

            base.Dispose();
        }

        private async Task SetSubscription(long? checkpoint = default, CancellationToken cancellationToken = default)
        {
            var streamStoreTask = _streamStoreFactory.GetStreamStore(cancellationToken: cancellationToken);
            var checkpointTask = checkpoint == default
                ? _getGlobalCheckpoint(cancellationToken)
                : Task.FromResult(checkpoint);

            _subscription?.Dispose();

            var streamStore = await streamStoreTask.ConfigureAwait(false);
            _subscription = streamStore.SubscribeToAll(await checkpointTask.ConfigureAwait(false),
                StreamMessageReceived, SubscriptionDropped);

            _logger.LogInformation("The global subscription has been created");
        }

        private async Task StreamMessageReceived(IAllStreamSubscription subscription, StreamMessage message,
            CancellationToken cancellationToken)
        {
            if (message.IsInSystemStream())
            {
                _logger.LogInformation(
                    "Event {messageType} is in a System stream therefore not being sent to observers", message.Type);
                return;
            }

            _logger.LogDebug("Event {eventType} received with ID {messageId}, sending to {observerLength} observers",
                message.Type, message.MessageId, _eventObservers.Length);

            var eventSourceEvent = await message.ToEvent(cancellationToken).ConfigureAwait(false);
            await Task.WhenAll(_eventObservers.Select(observer =>
                observer.EventReceived(eventSourceEvent, cancellationToken))).ConfigureAwait(false);

            await _setGlobalCheckpoint(subscription.LastPosition, cancellationToken).ConfigureAwait(false);
        }

        private void SubscriptionDropped(IAllStreamSubscription subscription, SubscriptionDroppedReason reason,
            Exception exception)
        {
            _failureCount++;
            if (_failureCount >= 5)
            {
                _logger.LogCritical(exception, "Event Subscription dropped. Reason: {reason}. Failure #{failureCount}.",
                    reason, _failureCount);
            }
            else
            {
                _logger.LogError(exception,
                    "Event Subscription dropped. Reason: {reason}. Failure #{failureCount}. Attempting to reconnect...",
                    reason, _failureCount);

                SetSubscription(subscription.LastPosition).Wait();
            }
        }
    }
}