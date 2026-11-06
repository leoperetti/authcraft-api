using System.Data;
using System.Data.Common;
using Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Health;

public sealed class DatabaseReadinessCheck : DatabaseReadinessCheck.IChecker
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private readonly AppDbContext _dbContext;

    public DatabaseReadinessCheck(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public interface IChecker
    {
        Task<bool> IsReadyAsync(CancellationToken cancellationToken);
    }

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(Timeout);

        var connection = _dbContext.Database.GetDbConnection();
        var openedByCheck = connection.State != ConnectionState.Open;
        try
        {
            if (openedByCheck)
            {
                await connection.OpenAsync(timeoutSource.Token);
            }

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.CommandTimeout = (int)Timeout.TotalSeconds;
            var result = await command.ExecuteScalarAsync(timeoutSource.Token);
            return result is not null && Convert.ToInt32(result) == 1;
        }
        catch (DbException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            if (openedByCheck && connection.State != ConnectionState.Closed)
            {
                await connection.CloseAsync();
            }
        }
    }
}
