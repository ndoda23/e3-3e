using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Lams.Application.Auditing;
using Lams.Application.Common;
using Lams.Domain.Users;
using Microsoft.AspNetCore.Routing;

namespace Lams.Web.Areas.Admin.Models;

/// <summary>Audit log filters bound from the query string (dates are local calendar days, both inclusive).</summary>
public sealed class AuditListQuery
{
    public string? Search { get; set; }

    /// <summary>The audited action (query key <c>event</c>: <c>action</c> is reserved for MVC routing).</summary>
    public string? Event { get; set; }

    public string? Entity { get; set; }

    public DateOnly? From { get; set; }

    public DateOnly? To { get; set; }

    /// <summary>Only records where this user acted.</summary>
    public Guid? User { get; set; }

    public int Page { get; set; } = 1;

    public AuditLogSearchQuery ToSearch() => new AuditLogSearchQuery
    {
        Search = Search,
        Action = Event,
        EntityType = Entity,
        From = From is { } from ? StartOfLocalDay(from) : null,
        To = To is { } to ? StartOfLocalDay(to.AddDays(1)).AddTicks(-1) : null,
        UserId = User is { } user ? new UserId(user) : null,
        Page = Page,
    }.Normalize();

    public RouteValueDictionary RouteValues(int? page = null)
    {
        var values = new RouteValueDictionary();
        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                values[key] = value.Trim();
            }
        }

        Add("search", Search);
        Add("event", Event);
        Add("entity", Entity);
        Add("from", From?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("to", To?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add("user", User?.ToString());
        if ((page ?? Page) > 1)
        {
            values["page"] = page ?? Page;
        }

        return values;
    }

    private static DateTimeOffset StartOfLocalDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local));
}

public sealed record AuditIndexViewModel(
    AuditListQuery Query,
    PagedResult<AuditLogListItem> Records,
    IReadOnlyList<string> Actions,
    IReadOnlyList<string> EntityTypes,
    string? UserFilterName);

/// <summary>One changed (or recorded) property of an audited entity.</summary>
public sealed record AuditPropertyChange(string Name, string? Before, string? After)
{
    public bool IsChanged => !string.Equals(Before, After, StringComparison.Ordinal);
}

/// <summary>
/// Before / after of an audit record, prepared for display: entity changes as a property table, lists (e.g. role
/// permissions) as removed / added items, anything else as formatted JSON.
/// </summary>
public sealed record AuditChanges(
    IReadOnlyList<AuditPropertyChange> Properties,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Added,
    string? RawBefore,
    string? RawAfter)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public bool IsPropertyTable => Properties.Count > 0;

    public bool IsList => Removed.Count > 0 || Added.Count > 0;

    public bool IsEmpty => !IsPropertyTable && !IsList && RawBefore is null && RawAfter is null;

    public static AuditChanges Build(string? beforeJson, string? afterJson)
    {
        var before = Parse(beforeJson);
        var after = Parse(afterJson);

        if ((before is null or JsonObject) && (after is null or JsonObject) && (before ?? after) is not null)
        {
            var b = before as JsonObject;
            var a = after as JsonObject;
            var names = (b?.Select(p => p.Key) ?? []).Union(a?.Select(p => p.Key) ?? [], StringComparer.Ordinal);
            var properties = names
                .Select(name => new AuditPropertyChange(name, Text(b, name), Text(a, name)))
                .ToList();
            return new AuditChanges(properties, [], [], null, null);
        }

        if (before is JsonArray beforeList && after is JsonArray afterList)
        {
            var oldItems = beforeList.Select(n => n?.ToString() ?? string.Empty).ToList();
            var newItems = afterList.Select(n => n?.ToString() ?? string.Empty).ToList();
            return new AuditChanges(
                [],
                oldItems.Except(newItems, StringComparer.Ordinal).ToList(),
                newItems.Except(oldItems, StringComparer.Ordinal).ToList(),
                null,
                null);
        }

        return new AuditChanges([], [], [], Pretty(before, beforeJson), Pretty(after, afterJson));
    }

    private static JsonNode? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            return JsonValue.Create(json);
        }
    }

    private static string? Text(JsonObject? obj, string name) =>
        obj is null || !obj.TryGetPropertyValue(name, out var value)
            ? null
            : value is null ? "null" : value is JsonValue ? value.ToString() : value.ToJsonString();

    private static string? Pretty(JsonNode? node, string? original) =>
        node switch
        {
            null => null,
            JsonValue value => value.ToString(),
            _ => node.ToJsonString(Indented),
        } ?? original;
}

public sealed record AuditDetailsViewModel(AuditLogDetails Record, AuditChanges Changes);
