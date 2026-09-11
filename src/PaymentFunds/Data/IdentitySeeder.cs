using Microsoft.AspNetCore.Identity;
using PaymentFunds.Models;

namespace PaymentFunds.Data;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;

        var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var config = sp.GetRequiredService<IConfiguration>();
        var logger = sp.GetRequiredService<ILogger<ApplicationDbContext>>();

        foreach (var role in new[] { "Requester", "Approver" })
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new IdentityRole(role));
            }
        }

        await EnsureUserAsync(userManager, config, logger, "requester@paymentfunds.local", "Riley Requester", "Requester");
        await EnsureUserAsync(userManager, config, logger, "approver@paymentfunds.local", "Avery Approver", "Approver");
    }

    private static async Task EnsureUserAsync(
        UserManager<ApplicationUser> userManager,
        IConfiguration config,
        ILogger logger,
        string email,
        string displayName,
        string role)
    {
        if (await userManager.FindByEmailAsync(email) is not null) return;
        
        var password = config[$"SeedUsers:{role}Password"];
        if (string.IsNullOrWhiteSpace(password))
        {
            logger.LogWarning("Skipping seed of {Email}: SeedUsers:{Role}Password is not configured", email, role);
            return;
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            DisplayName = displayName,
            EmailConfirmed = true
        };

        var result = await userManager.CreateAsync(user, password);
        if (result.Succeeded)
        {
            await userManager.AddToRoleAsync(user, role);
            logger.LogInformation("Seeded {Email} in role {Role}", email, role);
        }
        else
        {
            logger.LogError("Failed to seed {Email}: {Errors}", email,
            string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}