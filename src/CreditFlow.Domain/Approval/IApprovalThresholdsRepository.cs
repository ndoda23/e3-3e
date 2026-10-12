namespace CreditFlow.Domain.Approval;

/// <summary>Persistence of the single <see cref="ApprovalThresholds"/> row.</summary>
public interface IApprovalThresholdsRepository
{
    Task<ApprovalThresholds> GetAsync(CancellationToken cancellationToken);
}
