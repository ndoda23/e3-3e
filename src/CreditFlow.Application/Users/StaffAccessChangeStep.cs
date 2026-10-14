using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Users;

namespace CreditFlow.Application.Users;

/// <summary>
/// The single step through which every staff-member edit is saved. When the access-relevant state changed
/// (role, active flag, branch), it rotates the security stamp in the same transaction
/// and, after commit, removes the cached access profile. Edits that touch only other fields do neither.
/// </summary>
public sealed class StaffAccessChangeStep(
    IUnitOfWork unitOfWork,
    IIdentityService identity,
    IAccessCacheInvalidator accessCache)
{
    /// <param name="staffMember">The edited (tracked) staff member.</param>
    /// <param name="before">Snapshot of <see cref="StaffMember.Access"/> taken before the edit.</param>
    /// <param name="additionalWork">Other writes that must commit together with the edit (runs first).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<Result> SaveAsync(
        StaffMember staffMember,
        StaffAccess before,
        Func<CancellationToken, Task<Result>>? additionalWork,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(staffMember);
        ArgumentNullException.ThrowIfNull(before);

        var accessChanged = before != staffMember.Access;

        var result = await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                if (additionalWork is not null)
                {
                    var work = await additionalWork(token);
                    if (work.IsFailure)
                    {
                        return work;
                    }
                }

                await unitOfWork.SaveChangesAsync(token);

                return accessChanged
                    ? await identity.InvalidateSessionsAsync(staffMember.Id, token)
                    : Result.Success();
            },
            cancellationToken);

        // After commit, so that a concurrent request cannot re-cache the old profile from uncommitted state.
        if (result.IsSuccess && accessChanged)
        {
            await accessCache.InvalidateUserAsync(staffMember.Id, cancellationToken);
        }

        return result;
    }
}
