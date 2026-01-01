using Microsoft.AspNetCore.Identity;
using QuestionsHub.Blazor.Domain;

namespace QuestionsHub.Blazor.Data;

public static class DbSeeder
{
    /// <summary>
    /// Seeds the database with initial data including roles and admin user.
    /// </summary>
    public static async Task SeedAsync(
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IConfiguration configuration)
    {
        // First, seed roles
        await SeedRolesAsync(roleManager);

        // Then, seed admin user
        await SeedAdminUserAsync(userManager, configuration);
    }

    /// <summary>
    /// Seeds the three roles: Admin, Editor, User
    /// </summary>
    private static async Task SeedRolesAsync(RoleManager<IdentityRole> roleManager)
    {
        string[] roles = ["Admin", "Editor", "User"];

        foreach (var roleName in roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }
    }

    /// <summary>
    /// Seeds the initial admin user from configuration.
    /// Fails if AdminCredentials are not set.
    /// </summary>
    private static async Task SeedAdminUserAsync(UserManager<ApplicationUser> userManager, IConfiguration configuration)
    {
        var adminEmail = configuration["AdminCredentials:Email"];
        var adminPassword = configuration["AdminCredentials:Password"];

        if (string.IsNullOrWhiteSpace(adminEmail) || string.IsNullOrWhiteSpace(adminPassword))
        {
            throw new InvalidOperationException(
                "AdminCredentials:Email and AdminCredentials:Password must be set. " +
                "Application cannot start without admin credentials. " +
                "Please configure them in appsettings.Development.json (development) or as environment variables (production).");
        }

        // Check if admin user already exists
        var existingAdmin = await userManager.FindByEmailAsync(adminEmail);
        if (existingAdmin != null)
        {
            return; // Admin already exists
        }

        // Create admin user
        var adminUser = new ApplicationUser
        {
            UserName = adminEmail,
            Email = adminEmail,
            EmailConfirmed = true, // No email confirmation required
            FirstName = "Адміністратор",
            LastName = "Системи"
        };

        var result = await userManager.CreateAsync(adminUser, adminPassword);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidOperationException($"Failed to create admin user: {errors}");
        }

        // Assign Admin role
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}
