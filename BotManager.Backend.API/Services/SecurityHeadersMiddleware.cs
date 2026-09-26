namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// Adds defensive HTTP headers to API responses and enforces two CSRF/CSWSH guards:
    /// cookie-authenticated state-changing /api requests must carry the X-Requested-With header
    /// (cannot be set cross-site without CORS preflight), and hub connections must come from an
    /// allowed Origin.
    /// </summary>
    public class SecurityHeadersMiddleware
    {
        /// <summary>Header the SPA sends with every API request.</summary>
        public const string CsrfHeaderName = "X-Requested-With";

        /// <summary>Expected value of <see cref="CsrfHeaderName"/>.</summary>
        public const string CsrfHeaderValue = "BotManager";

        private readonly RequestDelegate _next;
        private readonly HashSet<string> _allowedOrigins;

        /// <summary>
        /// Creates the middleware with the configured CORS origins.
        /// </summary>
        public SecurityHeadersMiddleware(RequestDelegate next, IEnumerable<string> allowedOrigins)
        {
            _next = next;
            _allowedOrigins = new HashSet<string>(allowedOrigins, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Processes the request.
        /// </summary>
        public async Task InvokeAsync(HttpContext context)
        {
            var headers = context.Response.Headers;
            headers["X-Content-Type-Options"] = "nosniff";
            headers["X-Frame-Options"] = "DENY";
            headers["Referrer-Policy"] = "no-referrer";
            headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
            // The API only serves JSON: nothing may be loaded or framed from its responses.
            headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";

            var request = context.Request;

            if (request.Path.StartsWithSegments("/hubs") && !IsAllowedOrigin(request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            if (request.Path.StartsWithSegments("/api") && IsUnsafeMethod(request.Method) && HasAuthCookie(request)
                && !string.Equals(request.Headers[CsrfHeaderName], CsrfHeaderValue, StringComparison.Ordinal))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { message = "Missing CSRF header." });
                return;
            }

            await _next(context);
        }

        private bool IsAllowedOrigin(HttpRequest request)
        {
            var origin = request.Headers.Origin.ToString();
            if (string.IsNullOrEmpty(origin))
            {
                // Non-browser clients do not send Origin.
                return true;
            }

            if (_allowedOrigins.Contains(origin))
            {
                return true;
            }

            // Same-origin (e.g. SPA and API behind one reverse proxy).
            return Uri.TryCreate(origin, UriKind.Absolute, out var originUri)
                   && string.Equals(originUri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasAuthCookie(HttpRequest request)
            => request.Cookies.ContainsKey(SessionService.AccessCookieName)
               || request.Cookies.ContainsKey(SessionService.RefreshCookieName);

        private static bool IsUnsafeMethod(string method)
            => !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
    }
}
