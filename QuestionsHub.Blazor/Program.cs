using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using QuestionsHub.Blazor.Components;
using QuestionsHub.Blazor.Data;
using QuestionsHub.Blazor.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Add localization services
builder.Services.AddLocalization();

// Configure Entity Framework with PostgresSQL
builder.Services.AddDbContext<QuestionsHubDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

// Handle database reset in development mode (via --reset-db argument)
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

// Apply migrations and seed the database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<QuestionsHubDbContext>();
    await context.Database.MigrateAsync();
    await DbSeeder.SeedAsync(context);
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

app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();