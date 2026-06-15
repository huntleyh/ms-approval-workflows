using System.Security.Cryptography;
using System.Text;
using ApprovalWorkflow.Web.Config;
using ApprovalWorkflow.Web.Models;
using Microsoft.Extensions.Options;

namespace ApprovalWorkflow.Web.Services;

public class TokenService
{
    private readonly IDataService   _dataverse;
    private readonly AppSettings    _settings;

    public TokenService(IDataService dataverse, IOptions<AppSettings> options)
    {
        _dataverse = dataverse;
        _settings  = options.Value;
    }

    /// <summary>Generates a cryptographically secure single-use token.</summary>
    public (string rawToken, string tokenHash) GenerateToken()
    {
        var rawToken  = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        return (rawToken, ComputeSha256(rawToken));
    }

    /// <summary>
    /// Validates a raw token against ApprovalTierProgress.
    /// Returns null if not found, already consumed, or tier not Pending.
    /// </summary>
    public async Task<ApprovalTierProgress?> ValidateTokenAsync(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var hash     = ComputeSha256(rawToken);
        var progress = await _dataverse.GetTierProgressByTokenHashAsync(hash);

        if (progress is null)                                return null;
        if (progress.TokenConsumed)                          return null;
        if (progress.Status != TierStatus.Pending)           return null;

        // Check parent request expiry
        var request = await _dataverse.GetApprovalRequestAsync(progress.ApprovalRequestId);
        if (request is null || request.ExpiryDate < DateTime.UtcNow) return null;

        return progress;
    }

    /// <summary>Builds the review URL for a specific token.</summary>
    public string BuildReviewUrl(string rawToken)
        => $"{_settings.App.BaseUrl.TrimEnd('/')}/Review?token={rawToken}";

    /// <summary>Marks a tier progress token as consumed (single-use).</summary>
    public async Task ConsumeTokenAsync(Guid tierProgressId)
    {
        await _dataverse.UpdateTierProgressAsync(tierProgressId, new Dictionary<string, object>
        {
            ["cr_tokenconsumed"] = true
        });
    }

    public static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
