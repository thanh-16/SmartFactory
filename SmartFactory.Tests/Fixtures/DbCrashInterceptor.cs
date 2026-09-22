using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SmartFactory.Tests.Fixtures;

public class DbCrashInterceptor : SaveChangesInterceptor
{
    public bool TriggerFailure { get; set; } = true;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (TriggerFailure)
        {
            throw new InvalidOperationException("Simulated database failure during transaction execution.");
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
