using System;
using System.Threading;
using System.Threading.Tasks;
using LightestNight.System.EventSourcing.Events;
using LightestNight.System.EventSourcing.Observers;

namespace LightestNight.System.EventSourcing.SqlStreamStore.Subscriptions.Tests
{
    public class TestObserver : IEventObserver
    {
        private readonly Action<object> _outcome;
        
        public TestObserver(bool isActive, bool isReplaying, Action<object> outcome)
        {
            IsActive = isActive;
            IsReplaying = isReplaying;
            _outcome = outcome;
        }

        public Task InitialiseObserver(CancellationToken cancellationToken = new CancellationToken())
            => Task.CompletedTask;

        public Task EventReceived(EventSourceEvent evt, CancellationToken cancellationToken = default)
        {
            _outcome(evt);
            return Task.CompletedTask;
        }

        public bool IsActive { get; }
        public bool IsReplaying { get; }
    }
}