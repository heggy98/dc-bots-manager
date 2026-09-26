namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Names of the rate limiting policies registered in Program.cs.
    /// </summary>
    public static class RateLimitPolicies
    {
        /// <summary>Login and auth endpoints (per client IP).</summary>
        public const string Auth = "auth";

        /// <summary>Anonymous public endpoints (per client IP).</summary>
        public const string Public = "public";
    }
}
