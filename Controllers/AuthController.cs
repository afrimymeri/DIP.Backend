using System.Security.Claims;
using DIP.Backend.Data;
using DIP.Backend.Helpers;
using DIP.Backend.Interfaces;
using DIP.Backend.Models;
using DIP.Backend.Models.Auth;
using DIP.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DIP.Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<User> _hasher;
    private readonly ITokenService _tokens;
    private readonly IEmailService _email;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        ApplicationDbContext db,
        IPasswordHasher<User> hasher,
        ITokenService tokens,
        IEmailService email,
        ILogger<AuthController> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _email = email;
        _logger = logger;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest req)
    {
        req.Email = req.Email.Trim().ToLowerInvariant();
        req.Name = req.Name.Trim();
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.Name))
        {
            return BadRequest(new { message = "Name, Email and Password are required" });
        }

        // Validate password strength
        var (isValid, errors) = PasswordValidator.Validate(req.Password);
        if (!isValid)
        {
            return BadRequest(new { message = "Password does not meet requirements", errors });
        }

        var exists = await _db.Users.AnyAsync(u => u.Email == req.Email);
        if (exists)
        {
            return Conflict(new { message = "Email already exists" });
        }

        var user = new User
        {
            Name = req.Name,
            Email = req.Email,
            Role = Roles.User,
            EmailConfirmed = false,
            EmailConfirmationToken = Guid.NewGuid().ToString("N"),
            EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        user.PasswordHash = _hasher.HashPassword(user, req.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        // Send confirmation email (fire and forget, don't block registration)
        _ = Task.Run(async () =>
        {
            try
            {
                await _email.SendConfirmationEmailAsync(user.Email, user.Name, user.EmailConfirmationToken!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email to {Email}", user.Email);
            }
        });

        var (access, accessExp) = _tokens.CreateAccessToken(user);
        var (refresh, refreshExp) = _tokens.CreateRefreshToken(HttpContext.Connection.RemoteIpAddress?.ToString());
        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refresh,
            ExpiresAt = refreshExp,
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserId = user.Id
        });
        await _db.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            AccessToken = access,
            AccessTokenExpiresAt = accessExp,
            RefreshToken = refresh,
            RefreshTokenExpiresAt = refreshExp
        });
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.Include(u => u.RefreshTokens).FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return Unauthorized(new { message = "Invalid Credentials" });
        }

        var result = _hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Invalid credentials." });
        }

        var (access, accessExp) = _tokens.CreateAccessToken(user);
        var (refresh, refreshExp) = _tokens.CreateRefreshToken(HttpContext.Connection.RemoteIpAddress?.ToString());

        var ua = Request.Headers["User-Agent"].ToString();

        var activeTokens = user.RefreshTokens.Where(t => t.IsActive).ToList();
        foreach (var t in activeTokens)
        {
            t.RevokedAt = DateTime.UtcNow;
            t.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString();
            t.ReplacedByToken = refresh;
        }

        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = refresh,
            ExpiresAt = refreshExp,
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserId = user.Id
        });
        
        user.LastLoginAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return Ok(new AuthResponse
        {
            AccessToken = access,
            AccessTokenExpiresAt = accessExp,
            RefreshToken = refresh,
            RefreshTokenExpiresAt = refreshExp
        });
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Refresh(RefreshRequest req)
    {
        var token = await _db.RefreshTokens.Include(rt => rt.User)
            .FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken);
        if (token == null || !token.IsActive)
        {
            return Unauthorized(new { message = "Invalid refresh token" });
        }

        var user = token.User;
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString();

        var (newRefresh, newRefreshExp) = _tokens.CreateRefreshToken(HttpContext.Connection.RemoteIpAddress?.ToString());
        token.ReplacedByToken = newRefresh;
        _db.RefreshTokens.Add(new RefreshToken
        {
            Token = newRefresh,
            ExpiresAt = newRefreshExp,
            CreatedByIp = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserId = user.Id
        });
        
        var (access, accessExp) = _tokens.CreateAccessToken(user);
        await _db.SaveChangesAsync();

        return Ok(new AuthResponse
        {
            AccessToken = access,
            AccessTokenExpiresAt = accessExp,
            RefreshToken = newRefresh,
            RefreshTokenExpiresAt = newRefreshExp
        });
    }

    [Authorize]
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshRequest req)
    {
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(rt => rt.Token == req.RefreshToken);
        if (token == null || !token.IsActive)
        {
            return NotFound();
        }
        token.RevokedAt = DateTime.UtcNow;
        token.RevokedByIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<ActionResult<object>> Me()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ??
                               User.FindFirstValue(ClaimTypes.Name) ?? "0");
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(new { user.Id, user.Name, user.Email, user.Role, user.EmailConfirmed, user.LastLoginAt });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] string newPassword)
    {
        // Validate password strength
        var (isValid, errors) = PasswordValidator.Validate(newPassword);
        if (!isValid)
        {
            return BadRequest(new { message = "Password does not meet requirements", errors });
        }

        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null)
        {
            return NotFound();
        }

        user.PasswordHash = _hasher.HashPassword(user, newPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            // Don't reveal if email exists or not
            return NoContent();
        }

        user.EmailConfirmationToken = Guid.NewGuid().ToString("N");
        user.EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddHours(2);
        await _db.SaveChangesAsync();

        // Send password reset email (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _email.SendPasswordResetEmailAsync(user.Email, user.Name, user.EmailConfirmationToken!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
            }
        });

        return NoContent();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest req)
    {
        // Validate password strength
        var (isValid, errors) = PasswordValidator.Validate(req.NewPassword);
        if (!isValid)
        {
            return BadRequest(new { message = "Password does not meet requirements", errors });
        }

        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return BadRequest();
        }

        if (user.EmailConfirmationToken != req.Token || user.EmailConfirmationTokenExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Invalid or expired token" });
        }

        user.PasswordHash = _hasher.HashPassword(user, req.NewPassword);
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiresAt = null;
        user.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpPost("confirm-email")]
    [AllowAnonymous]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest req)
    {
        var email = req.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return BadRequest();
        }
        if (user.EmailConfirmationToken != req.Token || user.EmailConfirmationTokenExpiresAt < DateTime.UtcNow)
        {
            return BadRequest(new { message = "Invalid or expired token" });
        }

        user.EmailConfirmed = true;
        user.EmailConfirmationToken = null;
        user.EmailConfirmationTokenExpiresAt = null;
        await _db.SaveChangesAsync();
        return NoContent();
    }
    
    [HttpPost("resend-confirmation")]
    [AllowAnonymous]
    public async Task<IActionResult> ResendConfirmationEmail(ForgotPasswordRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email);

        if (user == null || user.EmailConfirmed)
        {
            return NoContent();
        }

        user.EmailConfirmationToken = Guid.NewGuid().ToString("N");
        user.EmailConfirmationTokenExpiresAt = DateTime.UtcNow.AddDays(2);
        await _db.SaveChangesAsync();

        _ = Task.Run(async () =>
        {
            try
            {
                await _email.SendConfirmationEmailAsync(user.Email, user.Name, user.EmailConfirmationToken!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "failed sending email to {Email}", user.Email);
            }
        });
        return NoContent();
    }
    
}