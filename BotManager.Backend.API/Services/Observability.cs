using System.Security.Cryptography;
using System.Text;
using BotManager.Backend.Bots.Services.Implementations;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace BotManager.Backend.API.Services
{
    /// <summary>
    /// OpenTelemetry setup: metrics (Prometheus scrape endpoint <c>/metrics</c> when <c>Metrics:Enabled</c>) and
    /// metrics + traces over OTLP when <c>OpenTelemetry:OtlpEndpoint</c> is configured.
    /// </summary>
    public static class Observability
    {
        /// <summary>Path of the Prometheus scrape endpoint.</summary>
        public const string MetricsPath = "/metrics";

        /// <summary>
        /// Registers the custom <see cref="BotManagerMetrics"/> and, when enabled, the OpenTelemetry pipeline.
        /// </summary>
        public static WebApplicationBuilder AddBotManagerObservability(this WebApplicationBuilder builder)
        {
            // Always registered: the Discord runtime records into it (no-op without a listener).
            builder.Services.AddSingleton<BotManagerMetrics>();

            var configuration = builder.Configuration;
            var prometheusEnabled = configuration.GetValue("Metrics:Enabled", false);
            var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"];
            var otlpEnabled = Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var otlpUri);
            if (!prometheusEnabled && !otlpEnabled)
            {
                return builder;
            }

            var otlpProtocol = string.Equals(configuration["OpenTelemetry:OtlpProtocol"], "http/protobuf", StringComparison.OrdinalIgnoreCase)
                ? OtlpExportProtocol.HttpProtobuf
                : OtlpExportProtocol.Grpc;
            void ConfigureOtlp(OtlpExporterOptions options)
            {
                options.Endpoint = otlpUri!;
                options.Protocol = otlpProtocol;
            }

            var otel = builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource.AddService(
                    configuration["OpenTelemetry:ServiceName"] ?? "botmanager-api"))
                .WithMetrics(metrics =>
                {
                    metrics
                        .AddMeter(BotManagerMetrics.MeterName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation()
                        .AddRuntimeInstrumentation();
                    if (prometheusEnabled)
                    {
                        metrics.AddPrometheusExporter();
                    }

                    if (otlpEnabled)
                    {
                        metrics.AddOtlpExporter(ConfigureOtlp);
                    }
                });

            // Traces are only useful with a collector (Prometheus has no traces).
            if (otlpEnabled)
            {
                otel.WithTracing(tracing => tracing
                    .AddAspNetCoreInstrumentation(options =>
                        options.Filter = context => !IsInfrastructurePath(context.Request.Path))
                    .AddHttpClientInstrumentation(options =>
                        // Discord webhook / interaction URLs embed secrets in the path: never export them.
                        options.FilterHttpRequestMessage = request =>
                            request.RequestUri?.AbsolutePath.Contains("/webhooks/", StringComparison.OrdinalIgnoreCase) != true)
                    .AddSqlClientInstrumentation()
                    .AddOtlpExporter(ConfigureOtlp));
            }

            return builder;
        }

        /// <summary>
        /// Maps the Prometheus scrape endpoint when <c>Metrics:Enabled</c> is true. With <c>Metrics:ApiKey</c> set, a matching
        /// <c>Authorization: Bearer</c> header is required; otherwise only direct (non-proxied) requests are served, e.g.
        /// Prometheus on the internal docker network. Rejected requests fall through to 404. Call after UseForwardedHeaders.
        /// </summary>
        public static WebApplication UseBotManagerMetricsEndpoint(this WebApplication app)
        {
            if (!app.Configuration.GetValue("Metrics:Enabled", false))
            {
                return app;
            }

            var apiKey = app.Configuration["Metrics:ApiKey"];
            app.UseOpenTelemetryPrometheusScrapingEndpoint(context =>
                context.Request.Path == MetricsPath && IsMetricsRequestAllowed(context.Request, apiKey));
            return app;
        }

        /// <summary>
        /// Whether a scrape request may read the metrics.
        /// </summary>
        internal static bool IsMetricsRequestAllowed(HttpRequest request, string? apiKey)
        {
            if (!string.IsNullOrEmpty(apiKey))
            {
                var header = request.Headers.Authorization.ToString();
                const string prefix = "Bearer ";
                if (!header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(header[prefix.Length..].Trim()),
                    Encoding.UTF8.GetBytes(apiKey));
            }

            // Anything that passed through a reverse proxy (nginx/Caddy) carries forwarded headers; UseForwardedHeaders
            // moves trusted ones to X-Original-*. Only direct requests on the internal network are allowed.
            var headers = request.Headers;
            return !headers.ContainsKey("X-Forwarded-For")
                && !headers.ContainsKey("X-Original-For")
                && !headers.ContainsKey("X-Forwarded-Proto")
                && !headers.ContainsKey("X-Original-Proto")
                && !headers.ContainsKey("Forwarded");
        }

        private static bool IsInfrastructurePath(PathString path)
            => path.StartsWithSegments(MetricsPath) || path.StartsWithSegments("/health");
    }
}
