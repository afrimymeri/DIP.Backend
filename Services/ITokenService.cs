using DIP.Backend.Models;

namespace DIP.Backend.Services;

public interface ITokenService
{
    (string token, DateTime expiresAt) CreateAccessToken(User user);
    (string token, DateTime expiresAt) CreateRefreshToken(string? ip);
}