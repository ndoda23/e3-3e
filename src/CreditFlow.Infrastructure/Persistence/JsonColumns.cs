using System.Text.Json;
using System.Text.Json.Serialization;
using CreditFlow.Domain.Applications;
using CreditFlow.Domain.Common;
using CreditFlow.Domain.Products;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CreditFlow.Infrastructure.Persistence;

/// <summary>
/// Value objects stored as one JSON column: nested or optional values that EF Core cannot map as complex types
/// (product terms with lists, the product snapshot, the exchange rate used, counter-offered terms, scoring runs,
/// rule definitions). They are always read and written as a whole and never queried inside SQL.
/// </summary>
internal static class JsonColumns
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    public static ValueConverter<T, string> Converter<T>() =>
        new(value => JsonSerializer.Serialize(value, Options), json => JsonSerializer.Deserialize<T>(json, Options)!);

    /// <summary>Compares by JSON, so changes inside lists are detected and snapshots are deep copies.</summary>
    public static ValueComparer<T> Comparer<T>() =>
        new(
            (a, b) => JsonSerializer.Serialize(a, Options) == JsonSerializer.Serialize(b, Options),
            value => JsonSerializer.Serialize(value, Options).GetHashCode(StringComparison.Ordinal),
            value => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value, Options), Options)!);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new MoneyJsonConverter());
        options.Converters.Add(new LoanTermsJsonConverter());
        return options;
    }

    /// <summary>Money as <c>{"amount":…,"currency":"GEL"}</c>, rebuilt through <see cref="Money.Of"/>.</summary>
    private sealed class MoneyJsonConverter : JsonConverter<Money>
    {
        public override Money Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<MoneyDto>(ref reader, Plain)!;
            return Money.Of(dto.Amount, dto.Currency);
        }

        public override void Write(Utf8JsonWriter writer, Money value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new MoneyDto(value.Amount, value.Currency), Plain);
    }

    /// <summary>Loan terms through <see cref="LoanTerms.Create"/> (the domain keeps its constructor private).</summary>
    private sealed class LoanTermsJsonConverter : JsonConverter<LoanTerms>
    {
        public override LoanTerms Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<LoanTermsDto>(ref reader, Plain)!;
            return LoanTerms.Create(Money.Of(dto.Amount, dto.Currency), dto.TermMonths, dto.AnnualRate, dto.ScheduleType).Value;
        }

        public override void Write(Utf8JsonWriter writer, LoanTerms value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(
                writer,
                new LoanTermsDto(value.Amount.Amount, value.Amount.Currency, value.TermMonths, value.AnnualRate, value.ScheduleType),
                Plain);
    }

    /// <summary>For the DTOs inside the converters (no custom converters, so no recursion).</summary>
    private static readonly JsonSerializerOptions Plain = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private sealed record MoneyDto(decimal Amount, Currency Currency);

    private sealed record LoanTermsDto(decimal Amount, Currency Currency, int TermMonths, decimal AnnualRate, ScheduleType ScheduleType);
}
