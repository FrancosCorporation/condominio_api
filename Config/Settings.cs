using System;
using System.Linq;
using System.Text;

namespace condominio_api
{
    /// <summary>
    /// Configurações sensíveis e de integração carregadas do ambiente.
    /// </summary>
    public static class Settings
    {
        private const string DevelopmentJunoUrl = "https://sandbox.boletobancario.com";
        private const string DevelopmentAppBaseUrl = "https://localhost:5001";

        public static string Secret
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("CONDOMINIO_JWT_SECRET");
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException("CONDOMINIO_JWT_SECRET é obrigatória.");
                if (Encoding.UTF8.GetByteCount(value) < 32)
                    throw new InvalidOperationException("CONDOMINIO_JWT_SECRET deve ter pelo menos 32 bytes UTF-8.");
                return value;
            }
        }

        public static string Token => Environment.GetEnvironmentVariable("JUNO_RESOURCE_TOKEN") ?? string.Empty;
        public static string Authorization => Environment.GetEnvironmentVariable("JUNO_AUTHORIZATION") ?? string.Empty;
        public static string PlanId => Environment.GetEnvironmentVariable("JUNO_PLAN_ID") ?? string.Empty;
        public static string SmtpUser => Environment.GetEnvironmentVariable("SMTP_USER") ?? string.Empty;
        public static string SmtpPassword => Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? string.Empty;
        public static string SmtpHost => Environment.GetEnvironmentVariable("SMTP_HOST") ?? "smtp.gmail.com";

        public static int SmtpPort
        {
            get
            {
                string value = Environment.GetEnvironmentVariable("SMTP_PORT");
                if (string.IsNullOrWhiteSpace(value))
                    return 587;
                if (!int.TryParse(value, out int port) || port < 1 || port > 65535)
                    throw new InvalidOperationException("SMTP_PORT deve estar entre 1 e 65535.");
                return port;
            }
        }

        public static string JunoBaseUrl =>
            Environment.GetEnvironmentVariable("JUNO_BASE_URL") ?? DevelopmentJunoUrl;

        public static string AppBaseUrl =>
            Environment.GetEnvironmentVariable("APP_BASE_URL") ?? DevelopmentAppBaseUrl;

        public static string[] AllowedOrigins =>
            (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ValidateOrigin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        public static void ValidateForEnvironment(bool isProduction)
        {
            _ = Secret;
            ValidateHttpsUrl(AppBaseUrl, "APP_BASE_URL");
            ValidateHttpsUrl(JunoBaseUrl, "JUNO_BASE_URL");

            if (!isProduction)
                return;

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("APP_BASE_URL")))
                throw new InvalidOperationException("APP_BASE_URL deve estar configurada em produção.");
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("JUNO_BASE_URL")) ||
                JunoBaseUrl.Contains("sandbox", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("JUNO_BASE_URL de produção deve ser configurada explicitamente.");
            if (AllowedOrigins.Length == 0)
                throw new InvalidOperationException("ALLOWED_ORIGINS deve conter ao menos uma origem em produção.");
            if (string.IsNullOrWhiteSpace(Token) || string.IsNullOrWhiteSpace(Authorization) || string.IsNullOrWhiteSpace(PlanId))
                throw new InvalidOperationException("JUNO_RESOURCE_TOKEN, JUNO_AUTHORIZATION e JUNO_PLAN_ID são obrigatórias em produção.");
            if (string.IsNullOrWhiteSpace(SmtpUser) || string.IsNullOrWhiteSpace(SmtpPassword))
                throw new InvalidOperationException("SMTP_USER e SMTP_PASSWORD são obrigatórias em produção.");
        }

        private static string ValidateOrigin(string origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
                uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') != origin.TrimEnd('/'))
                throw new InvalidOperationException($"ALLOWED_ORIGINS contém uma origem inválida: {origin}");

            return uri.GetLeftPart(UriPartial.Authority);
        }

        private static void ValidateHttpsUrl(string value, string name)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri) ||
                (uri.Scheme != Uri.UriSchemeHttps && !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
                throw new InvalidOperationException($"{name} deve ser uma URL HTTPS válida (HTTP é permitido somente para localhost).");
        }
    }
}
