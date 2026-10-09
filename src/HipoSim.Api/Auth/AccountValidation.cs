using System.Net.Mail;

namespace HipoSim.Api.Auth;

public static class AccountValidation
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static Dictionary<string, string[]> Validate(RegisterRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["The request body is required."];
            return errors;
        }
        if (string.IsNullOrWhiteSpace(request.FullName) || request.FullName.Trim().Length > 150)
            errors["fullName"] = ["Full name is required and cannot exceed 150 characters."];
        ValidateEmail(request.Email, errors);
        if (string.IsNullOrWhiteSpace(request.Phone) || request.Phone.Trim().Length > 20)
            errors["phone"] = ["Phone is required and cannot exceed 20 characters."];
        if (request.Password is null || request.Password.Length is < 8 or > 128)
            errors["password"] = ["Password must have between 8 and 128 characters."];
        if (request.AcceptedTerms != true)
            errors["acceptedTerms"] = ["Terms and Conditions must be accepted."];
        if (request.AcceptedDataProcessing != true)
            errors["acceptedDataProcessing"] = ["Personal data processing must be accepted."];
        return errors;
    }

    public static Dictionary<string, string[]> Validate(LoginRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["The request body is required."];
            return errors;
        }
        ValidateEmail(request.Email, errors);
        if (string.IsNullOrEmpty(request.Password))
            errors["password"] = ["Password is required."];
        return errors;
    }

    private static void ValidateEmail(string? email, Dictionary<string, string[]> errors)
    {
        var value = email?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 254 ||
            !MailAddress.TryCreate(value, out var parsed) || !string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase))
            errors["email"] = ["A valid email address is required."];
    }
}
