using Microsoft.Extensions.Hosting;
using System.Threading.Tasks;
using System.Threading;
using System;
using condominio_api.Models;
using MongoDB.Driver;
using RestSharp;
using System.Collections.Generic;
using Newtonsoft.Json;
using condominio_api.DependencyService;

namespace condominio_api.Services
{
    public class CheckPayment : IHostedService
    {
        private Timer _timer;
        private readonly MongoClient _clientMongoDb;
        private readonly IPaymentService _paymentService;
        private readonly TimeSpan _paymentCheckInterval;
        private int _checkRunning;
        private string _baseUrl = "https://sandbox.boletobancario.com";

        public CheckPayment(ICondominioDatabaseSetting setting, IPaymentService paymentService)
        {
            _paymentService = paymentService;
            var client = new MongoClient(setting.ConnectionString);
            _clientMongoDb = client;
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
            Console.WriteLine($"Conciliação de pagamentos configurada para {_paymentCheckInterval.TotalMinutes} minutos.");
            _timer = new Timer(consultCharges, null, TimeSpan.Zero, _paymentCheckInterval);
            return Task.CompletedTask;
        }

        public void changeIsPayment(IMongoDatabase mongoDatabase, UserAdm userAdm, bool isPayment)
        {
            if (userAdm == null || string.IsNullOrWhiteSpace(userAdm.id))
                throw new ArgumentException("O administrador a atualizar precisa ter um id.", nameof(userAdm));

            IMongoCollection<UserAdm> userAdmCollection = mongoDatabase.GetCollection<UserAdm>("usersAdm");
            var update = Builders<UserAdm>.Update.Set("isPayment", isPayment);
            var result = userAdmCollection.UpdateOne(user => user.id == userAdm.id, update);
            if (result.MatchedCount != 1)
                throw new InvalidOperationException("O administrador da assinatura não foi encontrado para atualizar o pagamento.");
        }
        public void insertHistoricoDocument(JunoCharge charge, IMongoDatabase mongoDatabase, UserAdm userAdm)
        {
            IMongoCollection<JunoHistorico> historicoCollection = mongoDatabase.GetCollection<JunoHistorico>("historicoPagamento");
            DateTimeOffset dueDateCharge = new DateTimeOffset(new DateTime(int.Parse(charge.dueDate.Split("-")[0]), int.Parse(charge.dueDate.Split("-")[1]), int.Parse(charge.dueDate.Split("-")[2])));
            List<JunoHistorico> historicoDocument = historicoCollection.Find<JunoHistorico>(JunoHistorico => true).ToList();

            if(historicoDocument.Count > 0)
            {
                if(dueDateCharge.ToUnixTimeSeconds() > historicoDocument[0].dueData)
                {
                    if(charge.status == "PAID")
                    {
                        JunoHistorico doc =  new JunoHistorico() {
                            idCharge = charge.subscription.id,
                            amount = charge.amount,
                            dueData = dueDateCharge.ToUnixTimeSeconds(),
                            status = charge.status,
                        };
                        historicoCollection.InsertOne(doc);

                        changeIsPayment(mongoDatabase, userAdm, true);
                    } else {
                        changeIsPayment(mongoDatabase, userAdm, false);
                    }
                }
            } else {
                if(charge.status == "PAID")
                {
                    JunoHistorico doc =  new JunoHistorico() {
                    idCharge = charge.subscription.id,
                    amount = charge.amount,
                    dueData = dueDateCharge.ToUnixTimeSeconds(),
                    status = charge.status,
                    };
                    historicoCollection.InsertOne(doc);
                    changeIsPayment(mongoDatabase, userAdm, true);
                } else {
                    changeIsPayment(mongoDatabase, userAdm, false);
                }
            }
        }

        public void consultCharges(Object state)
        {
            if (Interlocked.Exchange(ref _checkRunning, 1) == 1)
                return;

            try
            {
                dynamic _getAccessToken = _paymentService.getAccessToken();

                var client = new RestClient($"{_baseUrl}/api-integration/charges");
                var request = new RestRequest(Method.GET);
                request.AddHeaders(new Dictionary<string, string>() {
                    {"Authorization", $"Bearer {_getAccessToken.access_token}"},
                    {"X-Api-Version", "2"},
                    {"X-Resource-Token", Settings.Token},
                    {"Content-Type", "application/json;charset=UTF-8"}
                });

                IRestResponse response = client.Execute(request);
                JunoEmbedded _junoEmbedded = JsonConvert.DeserializeObject<JunoEmbedded>(response.Content);
                JunoCharges _junoCharges = _junoEmbedded?._embedded;

                IAsyncCursor<string> _listDatabaseNames = _clientMongoDb.ListDatabaseNames();
                foreach (var databaseName in _listDatabaseNames.ToList())
                {
                    if(databaseName != "admin" && databaseName != "config" && databaseName != "local" && databaseName != "userscondominio")
                    {
                        IMongoDatabase mongoDatabase = _clientMongoDb.GetDatabase(databaseName);
                        IMongoCollection<UserAdm> userAdmCollection = mongoDatabase.GetCollection<UserAdm>("usersAdm");
                        List<UserAdm> userAdm = userAdmCollection.Find<UserAdm>(user => true).ToList();

                        if(userAdm.Count > 0 && _junoCharges != null)
                        {
                            foreach(UserAdm administrator in userAdm)
                            {
                                foreach(JunoCharge charge in _junoCharges.charges ?? new List<JunoCharge>())
                                {
                                    if(charge.subscription != null && administrator.idSubscription == charge.subscription.id)
                                    {
                                        insertHistoricoDocument(charge, mongoDatabase, administrator);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Falha na conciliação de pagamentos: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _checkRunning, 0);
            }

        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _timer?.Change(Timeout.Infinite, Timeout.Infinite);
            _timer?.Dispose();
            return Task.CompletedTask;
        }

    }
}
