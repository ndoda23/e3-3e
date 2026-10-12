using System.Globalization;
using System.Reflection;
using System.Resources;
using System.Text.RegularExpressions;
using CreditFlow.Application.Users;
using CreditFlow.Domain.Auditing;
using CreditFlow.Domain.Roles;
using CreditFlow.Domain.Users;
using CreditFlow.Web;
using CreditFlow.Web.Areas.Admin.Models;
using CreditFlow.Web.Navigation;

namespace CreditFlow.IntegrationTests.Web;

/// <summary>Every UI text key used by the views exists in the English resources (no raw keys on screen).</summary>
public sealed partial class ResourceTests
{
    private static readonly ResourceManager Resources =
        new("CreditFlow.Web.Resources.SharedResource", typeof(SharedResource).Assembly);

    [GeneratedRegex("""T\["(?<key>[A-Za-z0-9_.]+)"\]""")]
    private static partial Regex LiteralKey();

    [Fact]
    public void Literal_keys_in_views_exist()
    {
        var webRoot = Path.Combine(RepositoryRoot(), "src", "CreditFlow.Web");
        var keys = Directory.EnumerateFiles(webRoot, "*.cshtml", SearchOption.AllDirectories)
            .SelectMany(file => LiteralKey().Matches(File.ReadAllText(file)).Select(m => (file, m.Groups["key"].Value)))
            .ToList();

        Assert.NotEmpty(keys);
        Assert.All(keys, k => Assert.True(Exists(k.Value), $"Missing resource '{k.Value}' used in {Path.GetFileName(k.file)}"));
    }

    [Fact]
    public void Composed_keys_exist()
    {
        var keys = new List<string> { "Nav.Section.Workspace", "Nav.Section.Administration" };
        keys.AddRange(Menu.Workspace.Concat(Menu.Administration).Select(i => $"Nav.{i.Key}"));
        keys.AddRange(Enum.GetNames<DashboardKind>().SelectMany(d => new[] { $"Dashboard.{d}", $"Dashboard.{d}.Description" }));
        keys.AddRange(Enum.GetNames<RoleKind>().SelectMany(k => new[] { $"RoleKind.{k}", $"RoleKind.{k}.Description", $"Roles.Sod{k}" }));
        keys.AddRange(Enum.GetNames<DataScope>().SelectMany(d => new[] { $"DataScope.{d}", $"DataScope.{d}.Description" }));
        keys.AddRange(Enum.GetNames<AttributeRule>().Select(r => $"AttributeRule.{r}"));
        keys.AddRange(Enum.GetNames<CreditFlow.Application.Applications.Inbox>().SelectMany(i => new[] { $"Inbox.{i}", $"Inbox.{i}.Description" }));
        keys.AddRange(["Applications.Failed", "Applications.FailedOverridable"]);
        keys.AddRange([
            "Applications.Disburse", "Applications.RetryPayout", "Applications.CheckPayout",
            "Applications.DisburseHint", "Applications.RetryHint", "Applications.CheckHint",
        ]);
        keys.AddRange(CreditFlow.Web.Models.WizardViewModel.StepKeys);
        keys.AddRange(CreditFlow.Application.ReferenceData.ScoringRuleSetSeed.Version1.Select(r => $"Rule.{r.Code}"));
        keys.AddRange(Enum.GetNames<UserStatusFilter>().Select(s => $"Users.Status.{s}"));
        keys.AddRange(Enum.GetNames<UserOperation>().SelectMany(o => new[]
        {
            $"Users.Confirm.{o}.Title", $"Users.Confirm.{o}.Description", $"Users.Confirm.{o}.Button",
        }));

        // Every audited action has a display name (constants of AuditActions).
        keys.AddRange(typeof(AuditActions).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => $"AuditAction.{f.GetRawConstantValue()}"));

        Assert.All(keys, key => Assert.True(Exists(key), $"Missing resource '{key}'"));
    }

    [Fact]
    public void Georgian_falls_back_to_English()
    {
        Assert.Equal("Dashboard", Resources.GetString("Nav.Dashboard", new CultureInfo("ka")));
    }

    private static bool Exists(string key) => Resources.GetString(key, CultureInfo.InvariantCulture) is not null;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CreditFlow.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (CreditFlow.sln) not found.");
    }
}
