using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading;

namespace BrickController2.Helpers
{
    public static class ObservableExtensions
    {
        public static IObservable<T> ObserveOnUsingNewEventLoopSchedulerOnBackground<T>(this IObservable<T> data) =>
            Observable.Using(() => new EventLoopScheduler(
                t => new Thread(t) { IsBackground = true, Name = $"Loop processing {nameof(T)}" }
            ), data.ObserveOn);
    }
}