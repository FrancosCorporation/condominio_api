using System;

namespace condominio_api
{
    /// <summary>
    /// Configurações sensíveis da API (chave JWT e credenciais do gateway Juno).
    /// Os valores vêm de variáveis de ambiente — nenhum segredo é versionado.
    ///
    /// Variáveis:
    ///   CONDOMINIO_JWT_SECRET   -> Settings.Secret
    ///   JUNO_RESOURCE_TOKEN     -> Settings.Token
    ///   JUNO_AUTHORIZATION      -> Settings.Authorization
    ///   JUNO_PLAN_ID            -> Settings.PlanId
    /// </summary>
    public static class Settings
    {
        private const string DevSecretFallback = "dev-only-insecure-secret-change-me-please!";

        public static string Secret =>
            Environment.GetEnvironmentVariable("CONDOMINIO_JWT_SECRET")
            ?? DevSecretFallback;

        public static string Token =>
            Environment.GetEnvironmentVariable("JUNO_RESOURCE_TOKEN") ?? string.Empty;

        public static string Authorization =>
            Environment.GetEnvironmentVariable("JUNO_AUTHORIZATION") ?? string.Empty;

        public static string PlanId =>
            Environment.GetEnvironmentVariable("JUNO_PLAN_ID") ?? string.Empty;
    }
}
