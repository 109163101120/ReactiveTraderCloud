using System.Collections.Generic;
using System.Threading.Tasks;

namespace Adaptive.ReactiveTrader.EventStore.Domain
{
    public interface IAggregateRepository
    {
        Task<TAggregate> GetByIdAsync<TAggregate>(object id) where TAggregate : IAggregate, new();
        Task<TAggregate> GetByIdOrCreateAsync<TAggregate>(object id) where TAggregate : class, IAggregate, new();
        Task<int> SaveAsync(AggregateBase aggregate, params KeyValuePair<string, string>[] extraHeaders);
    }
}