using System.Text.RegularExpressions;

namespace DIP.Backend.Helpers;

public static partial class PasswordValidator
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    public static (bool IsValid, List<string> Errors) Validate(string password)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(password))
        {
            errors.Add("Password is required");
            return (false, errors);
        }

        if (password.Length < MinLength)
            errors.Add($"Password must be at least {MinLength} characters long");

        if (password.Length > MaxLength)
            errors.Add($"Password must not exceed {MaxLength} characters");

        if (!HasUppercase().IsMatch(password))
            errors.Add("Password must contain at least one uppercase letter");

        if (!HasLowercase().IsMatch(password))
            errors.Add("Password must contain at least one lowercase letter");

        if (!HasDigit().IsMatch(password))
            errors.Add("Password must contain at least one digit");

        if (!HasSpecialChar().IsMatch(password))
            errors.Add("Password must contain at least one special character (!@#$%^&*(),.?\":{}|<>)");

        return (errors.Count == 0, errors);
    }

    [GeneratedRegex("[A-Z]")]
    private static partial Regex HasUppercase();

    [GeneratedRegex("[a-z]")]
    private static partial Regex HasLowercase();

    [GeneratedRegex("[0-9]")]
    private static partial Regex HasDigit();

    [GeneratedRegex("[!@#$%^&*(),.?\":{}|<>]")]
    private static partial Regex HasSpecialChar();
}
