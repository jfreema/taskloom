using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Taskloom.Infrastructure;

namespace Taskloom.Api.Tests.Support;

/// <summary>
/// Counts the trips a request makes to the database: every command EF sends (a SaveChanges batch is one command) and
/// every COMMIT or ROLLBACK. Npgsql sends BEGIN together with the next command, so it costs no trip of its own.
/// The SQL of each trip is kept for the failure message.
/// </summary>
public sealed class RoundTripCounter
{
    private readonly ConcurrentQueue<string> _trips = new();

    public int Count => _trips.Count;
    public IReadOnlyList<string> Trips => [.. _trips];

    public void Reset() => _trips.Clear();

    private void Add(string sql) => _trips.Enqueue(sql);

    /// <summary>Adds the counter's interceptors to the app's <see cref="TaskloomDbContext"/>.</summary>
    public void Register(IServiceCollection services) =>
        services.ConfigureDbContext<TaskloomDbContext>(o => o.AddInterceptors(new Commands(this), new Transactions(this)));

    private sealed class Commands(RoundTripCounter counter) : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            counter.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            counter.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        {
            counter.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<object> result, CancellationToken cancellationToken = default)
        {
            counter.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<int> NonQueryExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        {
            counter.Add(command.CommandText);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            counter.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Transactions(RoundTripCounter counter) : DbTransactionInterceptor
    {
        public override InterceptionResult TransactionCommitting(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            counter.Add("COMMIT");
            return result;
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            counter.Add("COMMIT");
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult TransactionRollingBack(DbTransaction transaction, TransactionEventData eventData, InterceptionResult result)
        {
            counter.Add("ROLLBACK");
            return result;
        }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            counter.Add("ROLLBACK");
            return ValueTask.FromResult(result);
        }
    }
}
