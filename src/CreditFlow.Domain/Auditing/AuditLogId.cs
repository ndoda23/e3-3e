namespace CreditFlow.Domain.Auditing;

/// <summary>Identifier of an audit record. Assigned by the database (identity column).</summary>
public readonly record struct AuditLogId(long Value)
{
    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
