using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using condominio_api.Models;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MongoDB.Driver;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;
using MongoDB.Bson;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using condominio_api.DependencyService;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace condominio_api.Services
{
    public class UserService : ControllerBase, IUserService
    {
        private readonly MongoClient _clientMongoDb;
        private readonly IMongoCollection<BsonDocument> _usedEmailTokens;
        private readonly ILogger<UserService> _logger;
        private const double EmailTokenLifetimeHours = 0.5;

        public UserService(ICondominioDatabaseSetting setting, ILogger<UserService> logger)
        {
            var client = new MongoClient(setting.ConnectionString);
            _clientMongoDb = client;
            _logger = logger;
            _usedEmailTokens = client.GetDatabase("userscondominio")
                .GetCollection<BsonDocument>("usedEmailTokens");
        }

        public BsonDocument RetornaUserAdm(UserAdm user)
        {
            if (user.image == null)
            {
                user.image = new byte[1];
            }
            return new BsonDocument{
                    {"_id", ObjectId.GenerateNewId()},
                    {"email", user.email.ToLower()},
                    {"password", passwordToHash(password: user.password)},
                    {"alg", UserGeneric.Bcrypt12Algorithm},
                    {"role", "Administrator"},
                    {"image", user.image},
                    {"datacreate", DateTimeOffset.Now.ToUnixTimeSeconds()},
                    {"nameCondominio", user.nameCondominio},
                    {"numero", user.numero},
                    {"estado", user.estado},
                    {"cidade", user.cidade},
                    {"rua", user.rua},
                    {"nome", user.nome},
                    {"cnpj", user.cnpj},
                    {"cep", user.cep},
                    {"verificado" , false},
                    {"isPayment" , false},
                    {"creditCardId" , ""},
                    {"idSubscription" , ""}
                };
        }
        public BsonDocument RetornaUserRef(UserGenericLogin user)
        {
            if (user.GetType() == new UserAdm().GetType())
            {
                return new BsonDocument{
                    {"_id", ObjectId.GenerateNewId()},
                    {"email", user.email.ToLower()},
                    {"databaseName", RemoverCaracterEspecial(user.nameCondominio)},
                    {"nameCondominio", user.nameCondominio},
                    {"role", "Administrator"}

                };
            }
            else if (user.GetType() == new UserPorteiro().GetType())
            {
                return new BsonDocument{
                    {"_id", ObjectId.GenerateNewId()},
                    {"email", user.email.ToLower()},
                    {"databaseName", RemoverCaracterEspecial(user.nameCondominio)},
                    {"nameCondominio", user.nameCondominio},
                    {"role", "Porteiro"}

                };
            }
            return null;

        }
        public BsonDocument RetornaUserPorteiro(UserPorteiro user)
        {
            if (user.image == null)
            {
                user.image = new byte[1];
            }
            return new BsonDocument{
                    {"_id", ObjectId.GenerateNewId()},
                    {"email", user.email.ToLower()},
                    {"password", passwordToHash(password: user.password)},
                    {"alg", UserGeneric.Bcrypt12Algorithm},
                    {"nameCondominio", user.nameCondominio },
                    {"nome", user.nome.ToLower() },
                    {"image", user.image},
                    {"role", "Porteiro"},
                    {"datacreate", DateTimeOffset.Now.ToUnixTimeSeconds()}
                };
        }
        public BsonDocument RetornaUserMorador(UserMorador user)
        {
            if (user.image == null)
            {
                user.image = new byte[1];
            }
            return new BsonDocument{
                    {"_id", ObjectId.GenerateNewId()},
                    {"email", user.email.ToLower()},
                    {"password", passwordToHash(password: user.password)},
                    {"alg", UserGeneric.Bcrypt12Algorithm},
                    {"nome", user.nome.ToLower() },
                    {"bloco", user.bloco.ToLower() },
                    {"numeroapartamento", user.numeroapartamento.ToLower() },
                    {"nameCondominio", user.nameCondominio },
                    {"image", user.image},
                    {"role", "Morador"},
                    {"datacreate", DateTimeOffset.Now.ToUnixTimeSeconds()}

                };
        }
        public Boolean EmailExist(UserGenericLogin user, IMongoClient database)
        {

            try
            {
                if (user is UserAdm || user is UserPorteiro)
                {

                    if (database.GetDatabase("userscondominio").GetCollection<UserReferencia>("users").Find(UserReferencia => UserReferencia.email == user.email).ToList().Count >= 1)
                    {
                        return true;
                    }
                }
                else if (user is UserMorador)
                {
                    if (database.GetDatabase(RemoverCaracterEspecial(user.nameCondominio)).GetCollection<UserMorador>("usersMoradores").Find(UserMorador => UserMorador.email == user.email).ToList().Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (System.Exception)
            {

                return false;

            }





        }
        public string passwordToHash(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("A senha não pode ficar vazia.", nameof(password));

            return BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
        }

        public bool VerificarSenha<TUser>(IMongoCollection<TUser> users, TUser user, string password)
            where TUser : UserGeneric
        {
            if (users == null || user == null || string.IsNullOrEmpty(password) || string.IsNullOrEmpty(user.password))
                return false;

            if (user.alg == UserGeneric.Bcrypt12Algorithm)
            {
                try
                {
                    return BCrypt.Net.BCrypt.Verify(password, user.password);
                }
                catch (Exception)
                {
                    return false;
                }
            }

            // Registros anteriores não têm marcador e usam SHA-256 com ASCII.
            if (!string.IsNullOrEmpty(user.alg) && user.alg != "sha256")
                return false;

            var legado = PasswordToLegacySha256(password);
            var senhaCorreta = CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(user.password),
                Encoding.UTF8.GetBytes(legado));
            if (!senhaCorreta)
                return false;

            var novoHash = passwordToHash(password);
            var resultado = users.UpdateOne(
                candidato => candidato.id == user.id,
                Builders<TUser>.Update
                    .Set(candidato => candidato.password, novoHash)
                    .Set(candidato => candidato.alg, UserGeneric.Bcrypt12Algorithm));

            if (resultado.MatchedCount != 1)
                return false;

            user.password = novoHash;
            user.alg = UserGeneric.Bcrypt12Algorithm;
            return true;
        }

        private static string PasswordToLegacySha256(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(password));
                StringBuilder resultado = new StringBuilder();
                for (int i = 0; i < hash.Length; i++)
                    resultado.Append(hash[i].ToString("X2"));
                return resultado.ToString();
            }
        }
        public string GenerateToken(UserGenericLogin user, double horas)
        {
            return CreateToken(user, horas, null);
        }

        public string GenerateEmailToken(UserGenericLogin user, string purpose)
        {
            if (purpose != "email_confirmation" && purpose != "password_reset")
                throw new ArgumentException("Finalidade de token de e-mail inválida.", nameof(purpose));
            return CreateToken(user, EmailTokenLifetimeHours, purpose);
        }

        private static string CreateToken(UserGenericLogin user, double horas, string purpose)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(Settings.Secret);
            var claims = new List<Claim>
            {
                new Claim("objectId", user.id ?? string.Empty),
                new Claim("nameCondominio", user.nameCondominio ?? string.Empty),
                new Claim(ClaimTypes.Role, user.role ?? string.Empty),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
            };
            if (purpose != null)
                claims.Add(new Claim("tokenPurpose", purpose));

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(horas),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
            };
            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }
        public Boolean ValidateToken(HttpRequest request)
        {
            string token = ExtractBearerToken(request);
            return token != null && TryValidateToken(token, out _, out _);
        }

        public JObject UnGenereteToken(HttpRequest request)
        {
            string token = ExtractBearerToken(request);
            if (token == null || !TryValidateToken(token, out _, out JwtSecurityToken validatedToken))
                throw new SecurityTokenException("Token Bearer ausente, inválido ou expirado.");

            return ToClaimsObject(validatedToken);
        }

        public bool TryConsumeEmailToken(string token, string expectedPurpose, out JObject claims)
        {
            claims = null;
            if (string.IsNullOrWhiteSpace(token) ||
                !TryValidateToken(token, out _, out JwtSecurityToken validatedToken))
                return false;

            string purpose = validatedToken.Claims.FirstOrDefault(claim => claim.Type == "tokenPurpose")?.Value;
            string tokenId = validatedToken.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Jti)?.Value;
            if (purpose != expectedPurpose || string.IsNullOrWhiteSpace(tokenId))
                return false;

            DateTime now = DateTime.UtcNow;
            _usedEmailTokens.DeleteMany(
                Builders<BsonDocument>.Filter.Lte("expiresAt", new BsonDateTime(now.AddMinutes(-1))));

            try
            {
                _usedEmailTokens.InsertOne(new BsonDocument
                {
                    { "_id", tokenId },
                    { "expiresAt", new BsonDateTime(validatedToken.ValidTo) }
                });
                claims = ToClaimsObject(validatedToken);
                return true;
            }
            catch (MongoWriteException exception) when (
                exception.WriteError?.Category == ServerErrorCategory.DuplicateKey)
            {
                return false;
            }
        }

        private static string ExtractBearerToken(HttpRequest request)
        {
            if (request == null ||
                !AuthenticationHeaderValue.TryParse(request.Headers["Authorization"].ToString(), out AuthenticationHeaderValue header) ||
                !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(header.Parameter))
                return null;

            return header.Parameter.Trim();
        }

        private static bool TryValidateToken(
            string token,
            out System.Security.Claims.ClaimsPrincipal principal,
            out JwtSecurityToken validatedToken)
        {
            principal = null;
            validatedToken = null;

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Settings.Secret)),
                    ValidateLifetime = true,
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidAlgorithms = new[] { SecurityAlgorithms.HmacSha256 },
                    ClockSkew = TimeSpan.FromSeconds(30)
                }, out SecurityToken securityToken);
                validatedToken = securityToken as JwtSecurityToken;
                return validatedToken != null;
            }
            catch (Exception exception) when (
                exception is SecurityTokenException ||
                exception is ArgumentException ||
                exception is FormatException)
            {
                principal = null;
                validatedToken = null;
                return false;
            }
        }

        private static JObject ToClaimsObject(JwtSecurityToken token)
        {
            string objectId = token.Claims.FirstOrDefault(claim => claim.Type == "objectId")?.Value;
            string role = token.Claims.FirstOrDefault(claim => claim.Type == "role")?.Value
                ?? token.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Role)?.Value;

            return new JObject
            {
                ["objectId"] = objectId,
                ["_id"] = objectId,
                ["nameCondominio"] = token.Claims.FirstOrDefault(claim => claim.Type == "nameCondominio")?.Value,
                ["role"] = role,
                ["jti"] = token.Claims.FirstOrDefault(claim => claim.Type == JwtRegisteredClaimNames.Jti)?.Value,
                ["tokenPurpose"] = token.Claims.FirstOrDefault(claim => claim.Type == "tokenPurpose")?.Value
            };
        }
        public string RemoverCaracterEspecial(string str)
        {
            /** Troca os caracteres acentuados por não acentuados **/
            string[] acentos = new string[] { "ç", "Ç", "á", "é", "í", "ó", "ú", "ý", "Á", "É", "Í", "Ó", "Ú", "Ý", "à", "è", "ì", "ò", "ù", "À", "È", "Ì", "Ò", "Ù", "ã", "õ", "ñ", "ä", "ë", "ï", "ö", "ü", "ÿ", "Ä", "Ë", "Ï", "Ö", "Ü", "Ã", "Õ", "Ñ", "â", "ê", "î", "ô", "û", "Â", "Ê", "Î", "Ô", "Û" };
            string[] semAcento = new string[] { "c", "C", "a", "e", "i", "o", "u", "y", "A", "E", "I", "O", "U", "Y", "a", "e", "i", "o", "u", "A", "E", "I", "O", "U", "a", "o", "n", "a", "e", "i", "o", "u", "y", "A", "E", "I", "O", "U", "A", "O", "N", "a", "e", "i", "o", "u", "A", "E", "I", "O", "U" };

            for (int i = 0; i < acentos.Length; i++)
            {
                str = str.Replace(acentos[i], semAcento[i]);
            }
            /** Troca os caracteres especiais da string por "" **/
            string[] caracteresEspeciais = { "¹", "²", "³", "£", "¢", "¬", "º", "¨", "\"", "'", ".", ",", "-", ":", "(", ")", "ª", "|", "\\\\", "°", "_", "@", "#", "!", "$", "%", "&", "*", ";", "/", "<", ">", "?", "[", "]", "{", "}", "=", "+", "§", "´", "`", "^", "~" };

            for (int i = 0; i < caracteresEspeciais.Length; i++)
            {
                str = str.Replace(caracteresEspeciais[i], "");
            }

            /** Troca os caracteres especiais da string por " " **/
            str = Regex.Replace(str, @"[^\w\.@-]", " ",
                                RegexOptions.None, TimeSpan.FromSeconds(1.5));

            return str.Trim().Replace(" ", "");
        }
        public string RemoverCaracterEspecialDeixarEspaco(string str)
        {
            /** Troca os caracteres acentuados por não acentuados **/
            string[] acentos = new string[] { "ç", "Ç", "á", "é", "í", "ó", "ú", "ý", "Á", "É", "Í", "Ó", "Ú", "Ý", "à", "è", "ì", "ò", "ù", "À", "È", "Ì", "Ò", "Ù", "ã", "õ", "ñ", "ä", "ë", "ï", "ö", "ü", "ÿ", "Ä", "Ë", "Ï", "Ö", "Ü", "Ã", "Õ", "Ñ", "â", "ê", "î", "ô", "û", "Â", "Ê", "Î", "Ô", "Û" };
            string[] semAcento = new string[] { "c", "C", "a", "e", "i", "o", "u", "y", "A", "E", "I", "O", "U", "Y", "a", "e", "i", "o", "u", "A", "E", "I", "O", "U", "a", "o", "n", "a", "e", "i", "o", "u", "y", "A", "E", "I", "O", "U", "A", "O", "N", "a", "e", "i", "o", "u", "A", "E", "I", "O", "U" };

            for (int i = 0; i < acentos.Length; i++)
            {
                str = str.Replace(acentos[i], semAcento[i]);
            }
            /** Troca os caracteres especiais da string por "" **/
            string[] caracteresEspeciais = { "¹", "²", "³", "£", "¢", "¬", "º", "¨", "\"", "'", ".", ",", "-", ":", "(", ")", "ª", "|", "\\\\", "°", "_", "@", "#", "!", "$", "%", "&", "*", ";", "/", "<", ">", "?", "[", "]", "{", "}", "=", "+", "§", "´", "`", "^", "~" };

            for (int i = 0; i < caracteresEspeciais.Length; i++)
            {
                str = str.Replace(caracteresEspeciais[i], "");
            }

            /** Troca os caracteres especiais da string por " " **/
            str = Regex.Replace(str, @"[^\w\.@-]", " ",
                                RegexOptions.None, TimeSpan.FromSeconds(1.5));

            return str;
        }
        public string RemoverBarraToken(string str)
        {
            return str.Replace("/", "");
        }
        public string BloquarUser(HttpRequest request)
        {
            String ip = request.HttpContext.Connection.RemoteIpAddress.ToString();
            Console.Write(ip);

            return "";
        }
        public async Task<bool> SendEmailAsync(ConstrucaoEmail messageModel)
        {
            try
            {
                string smtpUser = Settings.SmtpUser;
                string smtpPassword = Settings.SmtpPassword;
                if (string.IsNullOrWhiteSpace(smtpUser) || string.IsNullOrWhiteSpace(smtpPassword))
                    throw new InvalidOperationException("SMTP_USER e SMTP_PASSWORD devem estar configuradas.");

                using (SmtpClient client = new SmtpClient(Settings.SmtpHost, Settings.SmtpPort))
                using (MailMessage message = new MailMessage(smtpUser, messageModel.email))
                {
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(smtpUser, smtpPassword);
                    client.EnableSsl = true;
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    message.IsBodyHtml = true;
                    message.Subject = messageModel.titulo;
                    message.Body = messageModel.body;
                    await client.SendMailAsync(message);
                }
                return true;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falha no envio do e-mail transacional.");
                return false;
            }
        }

        public async Task<bool> EmailConfimacaoAsync(UserGenericLogin user)
        {
            return await EnviarEmailComLinkAsync(
                user,
                "Confirmação de Email",
                "api/confirmacaoEmail",
                "email_confirmation",
                "Confirme Seu Email");
        }

        public async Task<bool> EmailDeRedefinicaoDeSenhaAsync(UserGenericLogin user)
        {
            return await EnviarEmailComLinkAsync(
                user,
                "Redefinição de Senha",
                "api/recuperarSenhaCondominio",
                "password_reset",
                "Altere Sua Senha");
        }

        private async Task<bool> EnviarEmailComLinkAsync(
            UserGenericLogin user,
            string subject,
            string endpoint,
            string purpose,
            string linkText)
        {
            string url = $"{Settings.AppBaseUrl.TrimEnd('/')}/{endpoint}";
            ConstrucaoEmail email = new ConstrucaoEmail();
            email.email = user.email;
            email.titulo = subject;
            string image = "https://tse2.mm.bing.net/th?id=OIP.h3Eqt3wBHuM0tMbslVUcdwHaEo&pid=Api&P=0&w=300&h=300";
            string htmlimage = "<img src=" + image + " />";
            string token = Uri.EscapeDataString(GenerateEmailToken(user, purpose));
            string link = $"<p><a href='{url}?token={token}'>{linkText}</a></p>";
            string templatePath = Path.Combine(AppContext.BaseDirectory, "Templates", "confirmed.html");
            string html = await System.IO.File.ReadAllTextAsync(templatePath);
            html = html.Replace("<image/>", htmlimage);
            email.body = $"{html.Trim() + "\n" + link}";
            return await SendEmailAsync(email);
        }
        public UserAdm RetornaUserAdmPorId(string nameCondominio, string id)
        {
            IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(RemoverCaracterEspecial(nameCondominio));
            IMongoCollection<UserAdm> _users = _newDatabase.GetCollection<UserAdm>("usersAdm");
            return _users.Find(_user => _user.id == id).FirstOrDefault();
        }
        public BsonDocument GetBson(String nameDatabase, String nameitem, String nameCollection)
        {
            try
            {
                IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(RemoverCaracterEspecial(nameDatabase));

                if (nameCollection == "configApp")
                {
                    IMongoCollection<Agendamento> agend = _newDatabase.GetCollection<Agendamento>(nameCollection);
                    Agendamento agendam = agend.Find(agendam => agendam.itemNome == RemoverCaracterEspecialDeixarEspaco(nameitem)).ToList()[0];

                    if (agendam != null)
                    {
                        return agendam.ToBsonDocument();
                    }
                }
                else if (nameCollection == "usersAdm")
                {
                    IMongoCollection<UserAdm> user = _newDatabase.GetCollection<UserAdm>(nameCollection);
                    UserAdm user1 = user.Find(user1 => user1.id == RemoverCaracterEspecialDeixarEspaco(nameitem)).ToList()[0];

                    if (user1 != null)
                    {
                        return user1.ToBsonDocument();
                    }

                }
                else if (nameCollection == "usersPorteiros")
                {
                    IMongoCollection<UserPorteiro> user = _newDatabase.GetCollection<UserPorteiro>(nameCollection);
                    UserPorteiro user1 = user.Find(user1 => user1.id == RemoverCaracterEspecialDeixarEspaco(nameitem)).ToList()[0];

                    if (user1 != null)
                    {
                        return user1.ToBsonDocument();
                    }

                }
                else if (nameCollection == "usersMoradores")
                {
                    IMongoCollection<UserMorador> user = _newDatabase.GetCollection<UserMorador>(nameCollection);
                    UserMorador user1 = user.Find(user1 => user1.id == RemoverCaracterEspecialDeixarEspaco(nameitem)).ToList()[0];

                    if (user1 != null)
                    {
                        return user1.ToBsonDocument();
                    }

                }
                else if (nameCollection == "avisos")
                {
                    IMongoCollection<Aviso> user = _newDatabase.GetCollection<Aviso>(nameCollection);
                    Aviso aviso = user.Find(aviso => aviso.titulo == RemoverCaracterEspecialDeixarEspaco(nameitem)).ToList()[0];

                    if (aviso != null)
                    {
                        return aviso.ToBsonDocument();
                    }

                }


                return null;
            }
            catch
            {
                return null;
            }
        }
        public void GravaUserAdm(UserAdm user)
        {
            try
            {
                string nameCollection = "usersAdm";
                BsonDocument old = GetBson(user.nameCondominio, user.id, nameCollection);
                IMongoDatabase db = _clientMongoDb.GetDatabase(RemoverCaracterEspecial(user.nameCondominio));
                db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, user.ToBsonDocument());
            }
            catch (System.Exception e)
            {
                Console.Write(e);
            }

        }
        public bool verificaEmailConfirmado(string nameCondominio)
        {
            try
            {
                IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(RemoverCaracterEspecial(nameCondominio));
                UserAdm adm = _newDatabase.GetCollection<UserAdm>("usersAdm").Find(_user => true).ToList()[0];
                if (adm.verificado)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Console.Write(e);
                return false;
            }


        }
        public bool verificaPagamento(string nameCondominio)
        {
            try
            {
                IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(RemoverCaracterEspecial(nameCondominio));
                UserAdm adm = _newDatabase.GetCollection<UserAdm>("usersAdm").Find(_user => true).ToList()[0];
                if (adm.isPayment)
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (System.Exception e)
            {
                Console.Write(e);
                return false;
            }


        }

    }
}
