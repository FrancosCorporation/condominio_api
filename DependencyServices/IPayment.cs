using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using condominio_api.Models;

namespace condominio_api.DependencyService
{
    public interface IPaymentService
    {        
        bool isTokenExpiration();
        ActionResult Payment(string cardHash, HttpRequest request);
        dynamic getAccessToken();
        dynamic tokenizacao(string cardHash, JunoAccessToken junoAccessToken);
        dynamic subscription(HttpRequest requestUser, JunoAccessToken junoAccessToken, JunoTokenizacao junoTokenizacao);
        ActionResult modelResponse(dynamic modelJuno);
        ActionResult consultCharges(HttpRequest requestUser);
    }
}
