using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestionsHub.Blazor.Components;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Domain;
using QuestionsHub.Blazor.Infrastructure;

// Set Ukrainian culture as default for the entire application
var ukrainianCulture = new CultureInfo("uk-UA");
CultureInfo.DefaultThreadCurrentCulture = ukrainianCulture;
CultureInfo.DefaultThreadCurrentUICulture = ukrainianCulture;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add controllers for authentication API endpoints
builder.Services.AddControllers();

// Add HttpContextAccessor for accessing HTTP context in Blazor components
builder.Services.AddHttpContextAccessor();

// Add HttpClient factory for Blazor components to call API
builder.Services.AddHttpClient();

// Add localization services
builder.Services.AddLocalization();

// Configure Entity Framework with PostgresSQL
builder.Services.AddDbContext<QuestionsHubDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add DbContextFactory for Blazor Server components (avoids DbContext concurrency issues)
builder.Services.AddDbContextFactory<QuestionsHubDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")), ServiceLifetime.Scoped);

// Configure ASP.NET Core Identity
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
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

        // SignIn settings - no email confirmation required
        options.SignIn.RequireConfirmedEmail = false;
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<QuestionsHubDbContext>()
    .AddDefaultTokenProviders()
    .AddClaimsPrincipalFactory<CustomUserClaimsPrincipalFactory>();

// Configure authentication cookie
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.ExpireTimeSpan = TimeSpan.FromDays(30); // Session timeout: 30 days
    options.SlidingExpiration = true; // Extend cookie on activity
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.LogoutPath = "/Account/Logout";
});

// Add cascading authentication state for Blazor components
builder.Services.AddCascadingAuthenticationState();

// Configure media upload options
var mediaUploadOptions = new MediaUploadOptions();
builder.Configuration.GetSection(MediaUploadOptions.SectionName).Bind(mediaUploadOptions);

// Set media path based on environment
mediaUploadOptions.MediaPath = builder.Environment.IsDevelopment()
    ? Path.Combine(Directory.GetCurrentDirectory(), "..", "media")
    : "/app/media";

builder.Services.AddSingleton(mediaUploadOptions);
builder.Services.AddScoped<MediaService>();

var app = builder.Build();

// Handle database reset in development mode (via --reset-db argument)
#pragma warning disable CA1848 // Use LoggerMessage delegates - startup logging runs once, performance is not critical
if (app.Environment.IsDevelopment() && args.Contains("--reset-db"))
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();

    app.Logger.LogWarning("Resetting database (Development mode with --reset-db flag)...");

    try
    {
        // Try to delete the entire database (requires superuser privileges)
        await context.Database.EnsureDeletedAsync();
        app.Logger.LogInformation("Database deleted successfully.");
    }
    catch (Npgsql.PostgresException ex) when (ex.SqlState == "42501") // Permission denied
    {
        app.Logger.LogWarning("Cannot delete database (insufficient privileges). Dropping all tables instead...");

        // Alternative: Drop all tables (doesn't require database ownership)
        await context.Database.ExecuteSqlRawAsync("""
                                                              DO $$
                                                              DECLARE
                                                                  r RECORD;
                                                              BEGIN
                                                                  -- Drop all tables
                                                                  FOR r IN (SELECT tablename FROM pg_tables WHERE schemaname = 'public') LOOP
                                                                      EXECUTE 'DROP TABLE IF EXISTS ' || quote_ident(r.tablename) || ' CASCADE';
                                                                  END LOOP;

                                                                  -- Drop all sequences
                                                                  FOR r IN (SELECT sequence_name FROM information_schema.sequences WHERE sequence_schema = 'public') LOOP
                                                                      EXECUTE 'DROP SEQUENCE IF EXISTS ' || quote_ident(r.sequence_name) || ' CASCADE';
                                                                  END LOOP;
                                                              END $$;
                                                  """);

        app.Logger.LogInformation("All tables and sequences dropped successfully.");
    }
}
#pragma warning restore CA1848

// Apply migrations and seed the database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();

    await context.Database.MigrateAsync();
    await DbSeeder.SeedAsync(userManager, roleManager, configuration);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// Configure Ukrainian culture for localization
var supportedCultures = new[] { new CultureInfo("uk-UA") };
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("uk-UA"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

// Static files must be configured before routing
app.UseStaticFiles();

// Configure secure media file serving
var mediaPath = app.Environment.IsDevelopment()
    ? Path.Combine(Directory.GetCurrentDirectory(), "..", "media")
    : "/app/media";

if (!Directory.Exists(mediaPath))
{
    throw new InvalidOperationException(
        $"Media folder not found at '{mediaPath}'. " +
        "This is a configuration error. Please ensure the media folder is properly mounted.");
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaPath),
    RequestPath = "/media",
    OnPrepareResponse = ctx =>
    {
        // Only serve allowed media file types
        if (!MediaSecurityOptions.IsAllowedMediaFile(ctx.File.Name))
        {
            ctx.Context.Response.StatusCode = 404;
            ctx.Context.Response.ContentLength = 0;
            ctx.Context.Response.Body = Stream.Null;
            return;
        }

        // Security headers
        ctx.Context.Response.Headers.Append("X-Content-Type-Options", "nosniff");

        // Allow inline display for whitelisted media types
        ctx.Context.Response.Headers.Append("Content-Disposition", "inline");

        // Cache for 1 year (media files should be immutable or versioned)
        ctx.Context.Response.Headers.Append("Cache-Control", "public, max-age=31536000");
    }
});

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
