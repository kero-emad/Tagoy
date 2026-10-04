using System.Security.Claims;

namespace church.AIServices
{
    public interface ICurrentServiceCodeResolver
    {
        string? Resolve(ClaimsPrincipal user);
    }

    public class CurrentServiceCodeResolver :
        ICurrentServiceCodeResolver
    {
        private static readonly string[] CandidateClaimNames =
        {
            "serviceCode",
            "service_code",
            "ServiceCode",
            "service"
        };

        public string? Resolve(ClaimsPrincipal user)
        {
            foreach (var claimName in CandidateClaimNames)
            {
                var value = user.FindFirst(claimName)?.Value;

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value
                        .Trim()
                        .ToUpperInvariant();
                }
            }

            return null;
        }
    }
}
