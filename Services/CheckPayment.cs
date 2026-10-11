using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using condominio_api.DependencyService;
using condominio_api.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Newtonsoft.Json;

namespace condominio_api.Services
{
    public class CheckPayment : IHostedService
    {
        private static readonly HttpClient JunoHttpClient = new HttpClient();
        private readonly MongoClient _clientMongoDb;
        private readonly IPaymentService _paymentService;
        private readonly ILogger<CheckPayment> _logger;
        private readonly TimeSpan _paymentCheckInterval;
        private Timer _timer;
        private int _checkRunning;

        public CheckPayment(
            ICondominioDatabaseSetting setting,
            IPaymentService paymentService,
            ILogger<CheckPayment> logger)
        {
            _paymentService = paymentService;
            _logger = logger;
            _clientMongoDb = new MongoClient(setting.ConnectionString);
            _paymentCheckInterval = GetPaymentCheckInterval();
        }

        private static TimeSpan GetPaymentCheckInterval()
        {
            string configuredMinutes = Environment.GetEnvironmentVariable("PAYMENT_CHECK_MINUTES");
            if (string.IsNullOrWhiteSpace(configuredMinutes))
                return TimeSpan.FromMinutes(15);

            if (!int.TryParse(configuredMinutes, out int minutes) || minutes < 15 || minutes > 1440)
                throw new InvalidOperationException("PAYMENT_CHECK_MINUTES deve ser um inteiro entre 15 e 1440.");

            return TimeSpan.FromMinutes(minutes);
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation(
                "Conciliação de pagamentos configurada para {IntervalMinutes} minutos.",
                _paymentCheckInterval.TotalMinutes);
            _timer = new Timer(ConsultCharges, null, TimeSpan.Zero, _paymentCheckInterval);
            return Task.CompletedTask;
        }

        public void ChangeIsPayment(IMongoDatabase mongoDatabase, UserAdm userAdm, bool isPayment)
        {
            if (userAdm == null || string.IsNullOrWhiteSpace(userAdm.id))
                throw new ArgumentException("O administrador a atualizar precisa ter um id.", nameof(userAdm));

            IMongoCollection<UserAdm> userAdmCollection = mongoDatabase.GetCollection<UserAdm>("usersAdm");
            var update = Builders<UserAdm>.Update.Set(user => user.isPayment, isPayment);
            var result = userAdmCollection.UpdateOne(user => user.id == userAdm.id, update);
            if (result.MatchedCount != 1)
                throw new InvalidOperationException("O administrador da assinatura não foi encontrado para atualizar o pagamento.");
        }

        public void InsertHistoricoDocument(JunoCharge charge, IMongoDatabase mongoDatabase, UserAdm userAdm)
        {
            if (charge == null || string.IsNullOrWhiteSpace(charge.dueDate))
            {
                _logger.LogWarning("Cobrança sem dueDate ignorada. ChargeId: {ChargeId}", charge?.id);
                return;
            }

            if (!DateTimeOffset.TryParse(
                    charge.dueDate,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out DateTimeOffset dueDateCharge))
            {
                _logger.LogWarning(
                    "Cobrança com dueDate inválida ignorada. ChargeId: {ChargeId}; dueDate: {DueDate}",
                    charge.id,
                    charge.dueDate);
                return;
            }

            IMongoCollection<JunoHistorico> historicoCollection =
                mongoDatabase.GetCollection<JunoHistorico>("historicoPagamento");
            List<JunoHistorico> historicoDocument =
                historicoCollection.Find(_ => true).SortByDescending(item => item.dueData).ToList();

            bool hasNewDueDate = historicoDocument.Count == 0 ||
                dueDateCharge.ToUnixTimeSeconds() > (long)historicoDocument[0].dueData;
            if (!hasNewDueDate)
                return;

            if (charge.status == "PAID")
            {
                JunoHistorico doc = new JunoHistorico
                {
                    idCharge = charge.subscription.id,
                    amount = charge.amount,
                    dueData = dueDateCharge.ToUnixTimeSeconds(),
                    status = charge.status
                };
                historicoCollection.InsertOne(doc);
                ChangeIsPayment(mongoDatabase, userAdm, true);
            }
            else
            {
                ChangeIsPayment(mongoDatabase, userAdm, false);
            }
        }

        public void ConsultCharges(object state)
        {
            if (Interlocked.Exchange(ref _checkRunning, 1) == 1)
                return;

            try
            {
                List<(IMongoDatabase Database, UserAdm Administrator)> subscriptions = FindSubscriptions();
                if (subscriptions.Count == 0)
                {
                    _logger.LogInformation("Conciliação ignorada: nenhum condomínio tem assinatura Juno ativa.");
                    return;
                }

                dynamic accessToken = _paymentService.getAccessToken();
                if (accessToken == null || string.IsNullOrWhiteSpace((string)accessToken.access_token))
                    throw new InvalidOperationException("A Juno não retornou um token de acesso válido.");

                using var request = new HttpRequestMessage(
                    HttpMethod.Get,
                    $"{Settings.JunoBaseUrl.TrimEnd('/')}/api-integration/charges");
                AddJunoHeaders(request, (string)accessToken.access_token);

                using HttpResponseMessage response = JunoHttpClient.Send(request);
                string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    throw new HttpRequestException($"A consulta de cobranças Juno retornou HTTP {(int)response.StatusCode}.");

                JunoEmbedded responseModel = JsonConvert.DeserializeObject<JunoEmbedded>(content);
                JunoCharges charges = responseModel?._embedded;
                if (charges == null)
                    throw new InvalidOperationException("A resposta da Juno não contém a lista de cobranças.");

                foreach ((IMongoDatabase database, UserAdm administrator) in subscriptions)
                {
                    try
                    {
                        foreach (JunoCharge charge in charges.charges ?? new List<JunoCharge>())
                        {
                            if (charge?.subscription != null &&
                                administrator.idSubscription == charge.subscription.id)
                                InsertHistoricoDocument(charge, database, administrator);
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.LogError(
                            exception,
                            "Falha ao reconciliar pagamentos do condomínio {DatabaseName}.",
                            database.DatabaseNamespace.DatabaseName);
                    }
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falha na conciliação de pagamentos. O próximo ciclo continuará agendado.");
            }
            finally
            {
                Interlocked.Exchange(ref _checkRunning, 0);
            }
        }

        private List<(IMongoDatabase Database, UserAdm Administrator)> FindSubscriptions()
        {
            var subscriptions = new List<(IMongoDatabase, UserAdm)>();
            foreach (string databaseName in _clientMongoDb.ListDatabaseNames().ToList())
            {
                if (databaseName == "admin" || databaseName == "config" ||
                    databaseName == "local" || databaseName == "userscondominio")
                    continue;

                IMongoDatabase database = _clientMongoDb.GetDatabase(databaseName);
                List<UserAdm> administrators =
                    database.GetCollection<UserAdm>("usersAdm").Find(_ => true).ToList();
                foreach (UserAdm administrator in administrators)
                {
                    if (!string.IsNullOrWhiteSpace(administrator.idSubscription))
                        subscriptions.Add((database, administrator));
                }
            }
            return subscriptions;
        }

        private static void AddJunoHeaders(HttpRequestMessage request, string accessToken)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            request.Headers.TryAddWithoutValidation("X-Api-Version", "2");
            request.Headers.TryAddWithoutValidation("X-Resource-Token", Settings.Token);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            return Task.CompletedTask;
        }
    }
}
