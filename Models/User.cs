using Microsoft.AspNetCore.Authorization.Infrastructure;

namespace DIP.Backend.Models;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }

    public string PasswordHash { get; set; } = string.Empty;
    public bool EmailConfirmed { get; set; }
    public string? EmailConfirmationToken { get; set; }
    public DateTime? EmailConfirmationTokenExpiresAt { get; set; }

    public string Role { get; set; } = Roles.User;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = new List<RefreshToken>();
}

public static class Roles
{
    public const string User = "User";
    public const string Admin = "Admin";
}

/*keywords search edhe kthejn rezultat 

me kriju adaptera per punime shkencore(literature digjitale)

me automatizu procesin per punime shkencore.

science direct psh ose IEEE diqka mi qon email*/