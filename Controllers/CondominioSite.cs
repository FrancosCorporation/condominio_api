using Microsoft.AspNetCore.Mvc;
using condominio_api.Models;
using condominio_api.Services;
using Microsoft.AspNetCore.Authorization;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using System.Text.Encodings.Web;

namespace condominio_api.Controllers
{
    [Route("api")]
    [ApiController]
    public class CondominioControllerSite : ControllerBase
    {
        private readonly CondominioService _condominioService;

        public CondominioControllerSite(CondominioService condominioService)
        {
            _condominioService = condominioService;
        }

        // UsuarioAdm vai se cadastrar e cadastrar o banco
        [HttpPost("cadastroCondominio")]
        public async Task<IActionResult> RegisterCondominio([FromForm] UserAdm user) =>
           (IActionResult)await _condominioService.RegisterCondominio(user, Request);
        // o user adm vai logar por aqui
        [HttpPost("loginCondominio")]
        public ActionResult<dynamic> LoginCondominio([FromForm] UserGeneric user) => _condominioService.LoginCondominio(user, Request);

        [HttpPost("cadastroPorteiro")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<dynamic> RegisterPorteiro([FromForm] UserPorteiro user) =>
           _condominioService.RegisterPorteiro(user, Request);

        //adm vai cadastrar por essa rota
        [HttpPost("cadastroMorador")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<dynamic> RegisterMorador([FromForm] UserMorador user) => _condominioService.RegisterMorador(user, Request);

        [HttpPost("cadastroComunicado")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<dynamic> CadastroComunicado([FromForm] Aviso texto) => _condominioService.CadastroComunicado(texto, Request);

        [HttpPost("criarAgendamento")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<dynamic> CriarAgendamento([FromForm] Agendamento agend) => _condominioService.CriarAgendamento(agend, Request);

        [HttpPost("editFoto")]
        [Authorize]
        public ActionResult<dynamic> EnviarFoto([FromForm] byte[] foto) => _condominioService.EnviarFoto(foto, Request);

        [HttpPut("editAgendamento")]
        [Authorize(Roles = "Administrator")]
        public ActionResult<dynamic> EditarAgendamento([FromForm] Agendamento agend) => _condominioService.EditarAgendamento(agend, Request);

        [HttpGet("listaAgendamentos")]
        [Authorize]
        public ActionResult<dynamic> ListaAgendamentos() => _condominioService.ListaAgendamentos(Request);

        [HttpGet("listaItensAgendamentos")]
        [Authorize]
        public ActionResult<dynamic> ListaItensAgendamentos([FromForm] ObjectBase obj) => _condominioService.ListaItensAgendamentos(obj, Request);

        [HttpGet("configAgendamentos")]
        [Authorize]
        public ActionResult<dynamic> GetConfigAgendamentos([FromForm] ObjectBase obj) => _condominioService.GetConfigAgendamentos(obj, Request);

        [HttpGet("comunicados")]
        [Authorize]
        public ActionResult<dynamic> GetAgendamento() => _condominioService.GetAgendamento(Request);

        [HttpGet("confirmacaoEmail")]
        [AllowAnonymous]
        public ActionResult<dynamic> ConfirmacaoEmail([FromQuery][Required] string token) => _condominioService.ConfirmacaoEmail(token);

        [HttpPost("EmailNaoConfirmado")]
        public async Task<IActionResult> EmailNaoConfirmado([FromForm][Required] ConfirmacaoEmail confirm) =>
            (IActionResult)await _condominioService.EmailNaoConfirmado(confirm, Request);


        [HttpGet("recuperarSenhaCondominio")]
        [AllowAnonymous]
        public IActionResult FormularioRedefinicaoSenha([FromQuery][Required] string token)
        {
            Response.Headers["Cache-Control"] = "no-store";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            string safeToken = HtmlEncoder.Default.Encode(token);
            string safeAction = HtmlEncoder.Default.Encode(Request.Path.Value ?? "/api/recuperarSenhaCondominio");
            string html = $@"<!doctype html>
<html lang=""pt-BR"">
<head><meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1""><title>Redefinir senha</title></head>
<body>
  <main>
    <h1>Redefinir senha</h1>
    <form method=""post"" action=""{safeAction}"">
      <input type=""hidden"" name=""token"" value=""{safeToken}"">
      <label for=""password"">Nova senha</label>
      <input id=""password"" name=""senha"" type=""password"" autocomplete=""new-password"" required>
      <label for=""confirmation"">Confirme a nova senha</label>
      <input id=""confirmation"" type=""password"" autocomplete=""new-password"" required>
      <button type=""submit"">Salvar senha</button>
      <p id=""error"" role=""alert""></p>
    </form>
  </main>
  <script>
    const form = document.querySelector('form');
    form.addEventListener('submit', event => {{
      if (document.querySelector('#password').value !== document.querySelector('#confirmation').value) {{
        event.preventDefault();
        document.querySelector('#error').textContent = 'As senhas não conferem.';
      }}
    }});
  </script>
</body>
</html>";
            return Content(html, "text/html; charset=utf-8");
        }

        [HttpPost("recuperarSenhaCondominio")]
        [AllowAnonymous]
        public ActionResult<dynamic> EditarSenha([FromForm][Required] string token, [FromForm][Required] string senha) =>
            _condominioService.EditarSenha(token, senha);

        [HttpPut("alterarSenha")]
        [Authorize]
        public ActionResult<dynamic> AlterarSenha([FromQuery][Required] string senha) => _condominioService.AlterarSenha(senha, Request);


        [HttpPost("esqueciMinhaSenha")]
        public async Task<IActionResult> RedefinirSenha([FromForm][Required] RedefinirSenha red) =>
            (IActionResult)await _condominioService.RedefinirSenha(red, Request);

    }
}
