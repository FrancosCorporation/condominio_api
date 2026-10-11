using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Linq;
using condominio_api.DependencyService;
using condominio_api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace condominio_api.Services
{
    public class PaymentService : ControllerBase, IPaymentService
    {
        private static readonly HttpClient JunoHttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        private readonly IUserService _userService;
        private JunoAccessToken _validateAccessToken =
            new JunoAccessToken { dateTimeGenerateAccessToken = new DateTime(1970, 1, 1) };

        public PaymentService(IUserService userService)
        {
            _userService = userService;
        }

        public bool isTokenExpiration()
        {
            return DateTimeOffset.UtcNow - _validateAccessToken.dateTimeGenerateAccessToken >
                TimeSpan.FromHours(1);
        }

        public ActionResult Payment(string cardHash, HttpRequest request)
        {
            dynamic modelJuno = getAccessToken();
            if (modelJuno is not JunoError)
            {
                JunoAccessToken accessToken = (JunoAccessToken)modelJuno;
                dynamic tokenizationResult = tokenizacao(cardHash, accessToken);
                modelJuno = tokenizationResult;

                if (modelJuno is not JunoError)
                    modelJuno = subscription(request, accessToken, (JunoTokenizacao)tokenizationResult);
            }

            return modelResponse(modelJuno);
        }

        public dynamic getAccessToken()
        {
            if (!isTokenExpiration())
                return _validateAccessToken;

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Settings.JunoBaseUrl.TrimEnd('/')}/authorization-server/oauth/token");
            request.Headers.TryAddWithoutValidation("Authorization", Settings.Authorization);
            request.Content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials")
            });

            using HttpResponseMessage response = JunoHttpClient.Send(request);
            string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JunoAccessToken accessToken = JsonConvert.DeserializeObject<JunoAccessToken>(content);
                if (accessToken == null || string.IsNullOrWhiteSpace(accessToken.access_token))
                    throw new InvalidOperationException("A resposta da Juno não contém um token de acesso.");
                accessToken.dateTimeGenerateAccessToken = DateTimeOffset.UtcNow;
                _validateAccessToken = accessToken;
                return accessToken;
            }

            return JsonConvert.DeserializeObject<JunoError>(content) ?? new JunoError
            {
                status = (int)response.StatusCode
            };
        }

        public dynamic tokenizacao(string cardHash, JunoAccessToken junoAccessToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Settings.JunoBaseUrl.TrimEnd('/')}/api-integration/credit-cards/tokenization");
            AddJunoHeaders(request, junoAccessToken.access_token);
            request.Content = JsonContent(new { creditCardHash = cardHash });

            using HttpResponseMessage response = JunoHttpClient.Send(request);
            string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.OK)
                return JsonConvert.DeserializeObject<JunoTokenizacao>(content);

            return JsonConvert.DeserializeObject<JunoError>(content) ?? new JunoError
            {
                status = (int)response.StatusCode
            };
        }

        public dynamic subscription(
            HttpRequest requestUser,
            JunoAccessToken junoAccessToken,
            JunoTokenizacao junoTokenizacao)
        {
            var claims = _userService.UnGenereteToken(requestUser);
            UserAdm user = _userService.RetornaUserAdmPorId(
                claims["nameCondominio"].ToString(),
                claims["objectId"].ToString());
            if (user == null)
                return NotFound(new { message = "Administrador não encontrado." });
            user.creditCardId = junoTokenizacao.creditCardId;

            var subscriptionRequest = new
            {
                dueDay = DateTime.Now.Day,
                planId = Settings.PlanId,
                chargeDescription = "Inscrição plano Condominio",
                creditCardDetails = new
                {
                    creditCardId = junoTokenizacao.creditCardId
                },
                billing = new
                {
                    name = user.nome,
                    document = user.cnpj,
                    email = user.email,
                    address = new
                    {
                        street = user.rua,
                        number = user.numero,
                        city = user.cidade,
                        state = user.estado,
                        postCode = user.cep
                    }
                }
            };

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Settings.JunoBaseUrl.TrimEnd('/')}/api-integration/subscriptions");
            AddJunoHeaders(request, junoAccessToken.access_token);
            request.Content = JsonContent(subscriptionRequest);

            using HttpResponseMessage response = JunoHttpClient.Send(request);
            string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                JunoSubscription junoSubscription = JsonConvert.DeserializeObject<JunoSubscription>(content);
                if (junoSubscription == null || string.IsNullOrWhiteSpace(junoSubscription.id))
                    throw new InvalidOperationException("A resposta da Juno não contém uma assinatura válida.");
                user.idSubscription = junoSubscription.id;
                _userService.GravaUserAdm(user);
                return junoSubscription;
            }

            return JsonConvert.DeserializeObject<JunoError>(content) ?? new JunoError
            {
                status = (int)response.StatusCode
            };
        }

        public ActionResult modelResponse(dynamic modelJuno)
        {
            switch ((int)modelJuno.status)
            {
                case 400:
                    return BadRequest(modelJuno);
                case 401:
                    return Unauthorized(modelJuno);
                case 403:
                    return Forbid(modelJuno.details[0].message);
                case 500:
                    return Problem((string)modelJuno.details[0].message);
                default:
                    return Ok(modelJuno);
            }
        }

        public ActionResult consultCharges(HttpRequest requestUser)
        {
            var claims = _userService.UnGenereteToken(requestUser);
            UserAdm user = _userService.RetornaUserAdmPorId(
                claims["nameCondominio"].ToString(),
                claims["objectId"].ToString());
            if (user == null || string.IsNullOrWhiteSpace(user.idSubscription))
                return NotFound(new { message = "Assinatura não encontrada para este condomínio." });

            dynamic accessTokenResponse = getAccessToken();
            if (accessTokenResponse is JunoError accessTokenError)
                return StatusCode(accessTokenError.status, accessTokenError);

            JunoAccessToken accessToken = (JunoAccessToken)accessTokenResponse;
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Settings.JunoBaseUrl.TrimEnd('/')}/api-integration/charges");
            AddJunoHeaders(request, accessToken.access_token);

            using HttpResponseMessage response = JunoHttpClient.Send(request);
            string content = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, JsonConvert.DeserializeObject<JunoError>(content));

            JunoEmbedded responseModel = JsonConvert.DeserializeObject<JunoEmbedded>(content);
            List<JunoCharge> tenantCharges = (responseModel?._embedded?.charges ?? new List<JunoCharge>())
                .Where(charge => charge?.subscription?.id == user.idSubscription)
                .ToList();
            return Ok(tenantCharges);
        }

        private static void AddJunoHeaders(HttpRequestMessage request, string accessToken)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {accessToken}");
            request.Headers.TryAddWithoutValidation("X-Api-Version", "2");
            request.Headers.TryAddWithoutValidation("X-Resource-Token", Settings.Token);
        }

        private static StringContent JsonContent(object body)
        {
            return new StringContent(
                JsonConvert.SerializeObject(body),
                Encoding.UTF8,
                "application/json");
        }
    }
}
