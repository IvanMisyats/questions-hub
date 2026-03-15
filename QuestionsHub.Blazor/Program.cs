using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestionsHub.Blazor.Components;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;
using QuestionsHub.Blazor.Infrastructure.Auth;
using QuestionsHub.Blazor.Infrastructure.Email;
using QuestionsHub.Blazor.Infrastructure.Import;
using QuestionsHub.Blazor.Infrastructure.Media;
using QuestionsHub.Blazor.Infrastructure.Search;
using QuestionsHub.Blazor.Infrastructure.Telegram;

// Set Ukrainian culture as default for the entire application
var ukrainianCulture = new CultureInfo("uk-UA");
CultureInfo.DefaultThreadCurrentCulture = ukrainianCulture;
CultureInfo.DefaultThreadCurrentUICulture = ukrainianCulture;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services
    .AddCoreServices()
    .AddDatabase(builder.Configuration)
    .AddIdentityServices()
    .AddMediaServices(builder.Configuration, builder.Environment)
    .AddPackageImport(builder.Configuration)
    .AddEmailServices(builder.Configuration)
    .AddTelegramServices(builder.Configuration)
    .AddDataProtectionServices(builder.Environment);

var app = builder.Build();

// Validate production configuration
if (!app.Environment.IsDevelopment())
{
    var emailSettings = app.Configuration.GetSection(EmailSettings.SectionName).Get<EmailSettings>();
    if (string.IsNullOrEmpty(emailSettings?.ApiKey) || string.IsNullOrEmpty(emailSettings?.ApiSecret))
    {
        throw new InvalidOperationException(
            "Email:ApiKey and Email:ApiSecret must be configured in production. " +
            "Email confirmation cannot work without a configured email provider.");
    }
}

// Initialize database
await app.InitializeDatabase(args);

// Configure HTTP pipeline
app.ConfigureHttpPipeline();

app.Run();

// ============================================================================
// Service Registration Extensions
// ============================================================================

internal static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddRazorComponents().AddInteractiveServerComponents();
        services.AddControllersWithViews();

        services.AddHttpContextAccessor();
        services.AddHttpClient();
        services.AddLocalization();
        services.AddCascadingAuthenticationState();

        return services;
    }

    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");

        services.AddDbContext<QuestionsHubDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3)));

        services.AddDbContextFactory<QuestionsHubDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.EnableRetryOnFailure(maxRetryCount: 3)), ServiceLifetime.Scoped);

        services.AddHealthChecks()
            .AddDbContextCheck<QuestionsHubDbContext>("database");

        services.AddMemoryCache();

        services.AddScoped<SearchService>();
        services.AddScoped<AuthorService>();
        services.AddScoped<TagService>();
        services.AddScoped<PackageService>();
        services.AddScoped<PackageRenumberingService>();
        services.AddScoped<PackageManagementService>();
        services.AddScoped<AccessControlService>();
        services.AddScoped<PackageListService>();

        return services;
    }

    public static IServiceCollection AddIdentityServices(this IServiceCollection services)
    {
        services.AddIdentity<ApplicationUser, IdentityRole>(options =>
            {
                // Password settings
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequireNonAlphanumeric = true;
                options.Password.RequiredLength = 8;
                options.Password.RequiredUniqueChars = 1;

                // Lockout settings
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.AllowedForNewUsers = true;

                // User settings
                options.User.RequireUniqueEmail = true;

                // SignIn settings
                options.SignIn.RequireConfirmedEmail = true;
                options.SignIn.RequireConfirmedAccount = false;
            })
            .AddEntityFrameworkStores<QuestionsHubDbContext>()
            .AddDefaultTokenProviders()
            .AddClaimsPrincipalFactory<CustomUserClaimsPrincipalFactory>();

        services.ConfigureApplicationCookie(options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.ExpireTimeSpan = TimeSpan.FromDays(30);
            options.SlidingExpiration = true;
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.LogoutPath = "/Account/Logout";
        });

        return services;
    }

    public static IServiceCollection AddDataProtectionServices(
        this IServiceCollection services,
        IWebHostEnvironment environment)
    {
        var keysPath = environment.IsDevelopment()
            ? Path.Combine(Directory.GetCurrentDirectory(), "..", "keys")
            : "/app/keys";

        Directory.CreateDirectory(keysPath);

        services.AddDataProtection()
            .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
            .SetApplicationName("QuestionsHub");

        return services;
    }

    public static IServiceCollection AddEmailServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<EmailSettings>(configuration.GetSection(EmailSettings.SectionName));
        services.AddTransient<IEmailSender<ApplicationUser>, MailjetEmailSender>();

        return services;
    }

    public static IServiceCollection AddTelegramServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<TelegramSettings>(configuration.GetSection(TelegramSettings.SectionName));
        services.AddScoped<TelegramNotificationService>();

        return services;
    }
}

// ============================================================================
// Application Initialization Extensions
// ============================================================================

internal static class ApplicationExtensions
{
    public static async Task InitializeDatabase(this WebApplication app, string[] args)
    {
#pragma warning disable CA1848 // Startup logging runs once
        // Handle database reset in development mode
        if (app.Environment.IsDevelopment() && args.Contains("--reset-db"))
        {
            await ResetDatabase(app);
        }

        // Apply migrations and seed
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

        await context.Database.MigrateAsync();
        await DbSeeder.SeedAsync(userManager, roleManager, configuration);
#pragma warning restore CA1848
    }

    private static async Task ResetDatabase(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

        app.Logger.LogWarning("Resetting database (Development mode with --reset-db flag)...");

        try
        {
            await context.Database.EnsureDeletedAsync();
            app.Logger.LogInformation("Database deleted successfully.");
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42501")
        {
            app.Logger.LogWarning("Cannot delete database (insufficient privileges). Dropping all tables instead...");

            await context.Database.ExecuteSqlRawAsync("""
                DO $$
                DECLARE
                    r RECORD;
                BEGIN
                    FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
                        EXECUTE 'DROP TABLE IF EXISTS ' || quote_ident(r.tablename) || ' CASCADE';
                    END LOOP;
                    FOR r IN (SELECT sequence_name FROM information_schema.sequences WHERE sequence_schema = 'public') LOOP
                        EXECUTE 'DROP SEQUENCE IF EXISTS ' || quote_ident(r.sequence_name) || ' CASCADE';
                    END LOOP;
                END $$;
                """);

            app.Logger.LogInformation("All tables and sequences dropped successfully.");
        }
    }

    public static void ConfigureHttpPipeline(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
        }

        // Security headers
        app.Use(async (context, next) =>
        {
            context.Response.Headers.XContentTypeOptions = "nosniff";
            context.Response.Headers.XFrameOptions = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            context.Response.Headers.ContentSecurityPolicy = "frame-ancestors 'none'";
            await next();
        });

        // Ukrainian culture
        var supportedCultures = new[] { new CultureInfo("uk-UA") };
        app.UseRequestLocalization(new RequestLocalizationOptions
        {
            DefaultRequestCulture = new RequestCulture("uk-UA"),
            SupportedCultures = supportedCultures,
            SupportedUICultures = supportedCultures
        });

        app.ConfigureMediaFileServing();

        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseAntiforgery();

        app.MapHealthChecks("/health");
        app.MapStaticAssets();
        app.MapControllers();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();
    }

    private static void ConfigureMediaFileServing(this WebApplication app)
    {
        var handoutsPath = app.Environment.IsDevelopment()
            ? Path.Combine(Directory.GetCurrentDirectory(), "..", "uploads", "handouts")
            : "/app/uploads/handouts";

        if (!Directory.Exists(handoutsPath))
        {
            throw new InvalidOperationException(
                $"Handouts folder not found at '{handoutsPath}'. " +
                "Please ensure the uploads folder is properly mounted.");
        }

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = new PhysicalFileProvider(handoutsPath),
            RequestPath = "/media",
            OnPrepareResponse = ctx =>
            {
                if (!MediaSecurityOptions.IsAllowedMediaFile(ctx.File.Name))
                {
                    ctx.Context.Response.StatusCode = 404;
                    ctx.Context.Response.ContentLength = 0;
                    ctx.Context.Response.Body = Stream.Null;
                    return;
                }

                ctx.Context.Response.Headers.XContentTypeOptions = "nosniff";
                ctx.Context.Response.Headers.Append("Content-Disposition", "inline");
                ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000");
            }
        });
    }
}

