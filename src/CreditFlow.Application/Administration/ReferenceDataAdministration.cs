using CreditFlow.Application.Authorization;
using CreditFlow.Application.Common;
using CreditFlow.Domain.Approval;
using CreditFlow.Domain.Calendar;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Fx;
using CreditFlow.Domain.Scoring;

namespace CreditFlow.Application.Administration;

/// <summary>
/// Admin › Scoring rules. Versions are immutable: a change publishes the next version (the newest is used for new
/// scoring runs; every run records the version it used). The logic of each rule is code; a version holds which rules
/// apply, their parameters (JSON) and whether a failure can be overridden — validated by the <see cref="ScoringEngine"/>.
/// </summary>
public sealed class ScoringRuleAdministrationService(
    AdminGuard guard, IScoringRuleSetRepository ruleSets, ILendingAdminQueries queries, ScoringEngine engine,
    IUnitOfWork unitOfWork, TimeProvider clock)
{
    public Task<IReadOnlyList<RuleSetVersionItem>> ListAsync(CancellationToken cancellationToken) =>
        queries.ListRuleSetVersionsAsync(cancellationToken);

    public Task<ScoringRuleSet?> GetAsync(int version, CancellationToken cancellationToken) =>
        ruleSets.GetAsync(version, cancellationToken);

    public Task<ScoringRuleSet?> GetLatestAsync(CancellationToken cancellationToken) =>
        ruleSets.GetLatestAsync(cancellationToken);

    /// <summary>The rule codes the engine implements (a version may use any of them).</summary>
    public IReadOnlyCollection<string> KnownRuleCodes => engine.KnownCodes;

    /// <summary>Publishes <paramref name="rules"/> as the next version.</summary>
    public async Task<Result<int>> PublishAsync(IReadOnlyList<RuleDefinition> rules, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(rules);
        if (await guard.DenyUnlessAsync(Permissions.Admin.ScoringRulesManage, cancellationToken) is { } denied)
        {
            return Result.Failure<int>(denied);
        }

        var latest = await ruleSets.GetLatestAsync(cancellationToken);
        var next = latest is null
            ? ScoringRuleSet.Create(1, rules, engine, clock.GetUtcNow())
            : latest.PublishNext(rules, engine, clock.GetUtcNow());
        if (next.IsFailure)
        {
            return Result.Failure<int>(next.Error!);
        }

        if (latest is not null && latest.Rules.SequenceEqual(next.Value.Rules))
        {
            return Result.Failure<int>(AdministrationErrors.NothingChanged);
        }

        ruleSets.Add(next.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success(next.Value.Version);
    }
}

/// <summary>Admin › Approval thresholds (who decides which amount; how many committee votes decide).</summary>
public sealed class ApprovalThresholdsAdministrationService(
    AdminGuard guard, IApprovalThresholdsRepository thresholds, IUnitOfWork unitOfWork)
{
    public Task<ApprovalThresholds> GetAsync(CancellationToken cancellationToken) => thresholds.GetAsync(cancellationToken);

    public async Task<Result> ChangeAsync(
        decimal underwriterMaxGel, decimal seniorUnderwriterMaxGel, int requiredVotes, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permissions.Admin.ApprovalThresholdsManage, cancellationToken) is { } denied)
        {
            return Result.Failure(denied);
        }

        var current = await thresholds.GetAsync(cancellationToken);
        var changed = current.Change(underwriterMaxGel, seniorUnderwriterMaxGel, requiredVotes);
        if (changed.IsFailure)
        {
            return changed;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }
}

/// <summary>
/// Admin › FX rates: GEL per unit of USD / EUR from an effective date (scoring and submission use the latest rate on or
/// before their date). Rates are set by hand or imported from CSV; an import is all or nothing.
/// </summary>
public sealed class FxRateAdministrationService(
    AdminGuard guard, IFxRateRepository rates, ILendingAdminQueries queries, IUnitOfWork unitOfWork)
{
    private const string Permission = Permissions.Admin.FxRatesManage;

    public Task<PagedResult<FxRateItem>> ListAsync(Currency? currency, int page, CancellationToken cancellationToken) =>
        queries.ListFxRatesAsync(currency, Paging.NormalizePage(page), cancellationToken);

    /// <summary>Creates the rate of the day or changes it.</summary>
    public async Task<Result> SetAsync(Currency currency, DateOnly effectiveDate, decimal gelPerUnit, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure(denied);
        }

        var result = await ApplyAsync(currency, effectiveDate, gelPerUnit, cancellationToken);
        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<Result> DeleteAsync(Currency currency, DateOnly effectiveDate, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure(denied);
        }

        var rate = await rates.GetAsync(currency, effectiveDate, cancellationToken);
        if (rate is null)
        {
            return Result.Failure(AdministrationErrors.FxRateNotFound);
        }

        rates.Remove(rate);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Imports a CSV file (see <see cref="FxRateCsv"/>); any invalid line rejects the whole file.</summary>
    /// <returns>The number of rates created or changed.</returns>
    public async Task<Result<int>> ImportAsync(string csv, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure<int>(denied);
        }

        var parsed = FxRateCsv.Parse(csv);
        if (parsed.IsFailure)
        {
            return Result.Failure<int>(parsed.Error!);
        }

        return await unitOfWork.ExecuteInTransactionAsync(
            async token =>
            {
                var errors = new List<Error>();
                foreach (var row in parsed.Value)
                {
                    var applied = await ApplyAsync(row.Currency, row.EffectiveDate, row.GelPerUnit, token);
                    if (applied.IsFailure)
                    {
                        errors.Add(FxRateCsv.LineError(row.Line, applied.Error!.Description));
                    }
                }

                if (errors.Count > 0)
                {
                    return Result.Failure<int>(Error.ValidationFailed(errors));
                }

                await unitOfWork.SaveChangesAsync(token);
                return Result.Success(parsed.Value.Count);
            },
            cancellationToken);
    }

    private async Task<Result> ApplyAsync(Currency currency, DateOnly effectiveDate, decimal gelPerUnit, CancellationToken cancellationToken)
    {
        var existing = await rates.GetAsync(currency, effectiveDate, cancellationToken);
        if (existing is not null)
        {
            return existing.ChangeRate(gelPerUnit);
        }

        var created = FxRate.Create(currency, effectiveDate, gelPerUnit);
        if (created.IsFailure)
        {
            return Result.Failure(created.Error!);
        }

        rates.Add(created.Value);
        return Result.Success();
    }
}

/// <summary>Admin › Holidays: the public holidays that do not count as working days (escalation deadlines).</summary>
public sealed class HolidayAdministrationService(
    AdminGuard guard, IHolidayRepository holidays, ILendingAdminQueries queries, IUnitOfWork unitOfWork)
{
    private const string Permission = Permissions.Admin.HolidaysManage;

    public Task<IReadOnlyList<HolidayItem>> ListAsync(int year, CancellationToken cancellationToken) =>
        queries.ListHolidaysAsync(year, cancellationToken);

    public async Task<Result> AddAsync(DateOnly date, string? name, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure(denied);
        }

        if (await holidays.GetAsync(date, cancellationToken) is not null)
        {
            return Result.Failure(CalendarErrors.DuplicateHoliday);
        }

        var created = Holiday.Create(date, name);
        if (created.IsFailure)
        {
            return Result.Failure(created.Error!);
        }

        holidays.Add(created.Value);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<Result> RenameAsync(DateOnly date, string? name, CancellationToken cancellationToken) =>
        ChangeAsync(date, holiday => holiday.Rename(name), cancellationToken);

    public Task<Result> RemoveAsync(DateOnly date, CancellationToken cancellationToken) =>
        ChangeAsync(date, holiday =>
        {
            holidays.Remove(holiday);
            return Result.Success();
        }, cancellationToken);

    private async Task<Result> ChangeAsync(DateOnly date, Func<Holiday, Result> change, CancellationToken cancellationToken)
    {
        if (await guard.DenyUnlessAsync(Permission, cancellationToken) is { } denied)
        {
            return Result.Failure(denied);
        }

        var holiday = await holidays.GetAsync(date, cancellationToken);
        if (holiday is null)
        {
            return Result.Failure(CalendarErrors.HolidayNotFound);
        }

        var result = change(holiday);
        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}

public static class AdministrationErrors
{
    public static readonly Error NothingChanged = Error.Conflict(
        "Administration.NothingChanged", "Nothing was changed, so no new version was published.");

    public static readonly Error FxRateNotFound = Error.NotFound("FxRate.NotFound", "The FX rate was not found.");

    public static readonly Error DateRequired = Error.Validation("Administration.DateRequired", "Enter the date.", "Date");
}
