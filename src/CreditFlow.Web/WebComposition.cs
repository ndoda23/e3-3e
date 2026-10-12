using System.Globalization;
using CreditFlow.Application.Common;
using CreditFlow.Web.Authentication;
using CreditFlow.Web.Authorization;
using CreditFlow.Web.Infrastructure;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;
using Serilog;

namespace CreditFlow.Web;

/// <summary>Registration and pipeline of the web layer (composition root helpers used by Program.cs).</summary>
public static class WebComposition
{
    public const string AuthCookieName = "__Host-CreditFlow.Auth";
    public const string TwoFactorCookieName = "__Host-CreditFlow.2fa";
    public const string TwoFactorRememberCookieName = "__Host-CreditFlow.2fa-remember";
    public const string AntiforgeryCookieName = "__Host-CreditFlow.Af";
    public const string AntiforgeryHeaderName = "RequestVerificationToken";

    /// <summary>Rate-limit policy for sign-in posts (password, 2FA code, recovery code).</summary>
    public const string SignInRateLimitPolicy = "sign-in";

    public static readonly CultureInfo[] SupportedCultures = [new("en"), new("ka")];

    public static IServiceCollection AddWeb(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<ICurrentUser, HttpCurrentUser>());

        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture("en");
            options.SupportedCultures = SupportedCultures;
            options.SupportedUICultures = SupportedCultures;

            // English first: only an explicit culture choice (cookie) switches language, not the browser setting.
            options.RequestCultureProviders = [new CookieRequestCultureProvider()];
        });

        services
            .AddControllersWithViews(options =>
            {
                options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
                options.Filters.Add<ForcedAccountSetupFilter>();
            })
            .AddViewLocalization()
            .AddDataAnnotationsLocalization(options =>
                options.DataAnnotationLocalizerProvider = (_, factory) => factory.Create(typeof(SharedResource)));

        AddAuthorization(services);
        AddCookies(services);
        AddSignInRateLimiting(services);

        services.AddAntiforgery(options =>
        {
            options.HeaderName = AntiforgeryHeaderName;
            options.Cookie.Name = AntiforgeryCookieName;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.HttpOnly = true;
        });

        services.AddProblemDetails();
        services.AddExceptionHandler<GlobalExceptionHandler>();

        services.AddHsts(options =>
        {
            options.MaxAge = TimeSpan.FromDays(365);
            options.IncludeSubDomains = true;
        });

        return services;
    }

    private static void AddAuthorization(IServiceCollection services)
    {
        services.AddScoped<CurrentAccess>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddScoped<IAuthorizationHandler, AdministrationHandler>();
        services.AddScoped<IAuthorizationHandler, DataScopeHandler>();
        services.AddScoped<IAuthorizationHandler, CreditDecisionHandler>();
        services.AddScoped<IAuthorizationHandler, PricingExceptionApprovalHandler>();

        services.AddAuthorizationBuilder()
            // Every endpoint requires a signed-in user unless it is explicitly [AllowAnonymous].
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Administration, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(AdministrationRequirement.Instance));
    }

    private static void AddSignInRateLimiting(IServiceCollection services)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy(SignInRateLimitPolicy, context =>
            {
                var permitLimit = context.RequestServices.GetRequiredService<IOptions<SecurityOptions>>().Value.SignInAttemptsPerMinute;
                return RateLimitPartition.GetFixedWindowLimiter(
                    context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });
    }

    private static void AddCookies(IServiceCollection services)
    {
        services.AddScoped<CreditFlowCookieEvents>();

        services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
            .Configure<IOptions<SecurityOptions>, TimeProvider>((options, security, clock) =>
            {
                options.Cookie.Name = AuthCookieName;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.ExpireTimeSpan = security.Value.IdleTimeout;
                options.SlidingExpiration = true;
                options.LoginPath = "/Account/Login";
                options.LogoutPath = "/Account/Logout";
                options.AccessDeniedPath = "/Account/AccessDenied";
                options.EventsType = typeof(CreditFlowCookieEvents);
                options.TimeProvider = clock;
            });

        foreach (var (scheme, name) in new[]
                 {
                     (IdentityConstants.TwoFactorUserIdScheme, TwoFactorCookieName),
                     (IdentityConstants.TwoFactorRememberMeScheme, TwoFactorRememberCookieName),
                 })
        {
            services.AddOptions<CookieAuthenticationOptions>(scheme).Configure<TimeProvider>((options, clock) =>
            {
                options.Cookie.Name = name;
                options.Cookie.HttpOnly = true;
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.Path = "/";
                options.TimeProvider = clock;
            });
        }
    }

    public static WebApplication UseCreditFlow(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseExceptionHandler("/Error");
        app.UseStatusCodePagesWithReExecute("/Error/{0}");
        if (!app.Environment.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseHttpsRedirection();
        app.UseMiddleware<SecurityHeadersMiddleware>();
        app.UseStaticFiles();
        app.UseSerilogRequestLogging();
        app.UseRequestLocalization();
        app.UseRouting();
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapControllerRoute("areas", "{area:exists}/{controller=Users}/{action=Index}/{id?}");
        app.MapControllerRoute("default", "{controller=Dashboard}/{action=Index}/{id?}");

        return app;
    }
}
