using condominio_api.Models;
using MongoDB.Driver;
using System.Collections.Generic;
using MongoDB.Bson;
using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Atividio.Validadores.Cnpj;
using condominio_api.DependencyService;

namespace condominio_api.Services
{
    public class CondominioService : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ObjectsService objectsService = new ObjectsService();
        private readonly CnpjValidador cnpjValidador = new CnpjValidador();

        private readonly MongoClient _clientMongoDb;
        private double _timeExpiredTokenLogin = 10;
        
        public CondominioService() {}
        public CondominioService(ICondominioDatabaseSetting setting, IUserService userService)
        {
            var client = new MongoClient(setting.ConnectionString);
            _clientMongoDb = client;
            _userService = userService;
            // _condominiosDatabase = client.GetDatabase(setting.DatabaseName);
        }
        public List<string> GetListNameDatabase()
        {
            List<string> listNameDatabase = new List<string>();
            using (var cursor = _clientMongoDb.ListDatabaseNames())
            {
                while (cursor.MoveNext())
                {
                    foreach (var current in cursor.Current)
                    {
                        if (current != "admin" && current != "config" && current != "local" && current != "userscondominio") listNameDatabase.Add(current);
                    }
                }
            }

            return listNameDatabase;
        }
        public dynamic ListaAgendamentos(HttpRequest request)
        {
            JObject jsonClaim = _userService.UnGenereteToken(request);
            string nameCondominio = jsonClaim["nameCondominio"].ToString();
            if (_userService.ValidateToken(request))
            {
                IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(nameCondominio));
                {

                    List<string> listNameCollection = new List<string>();

                    using (var cursor = _newDatabase.ListCollectionNames())
                    {
                        while (cursor.MoveNext())
                        {
                            foreach (var current in cursor.Current)
                            {
                                if (current != "avisos" && current != "configApp" && current != "usersAdm" && current != "usersPorteiros" && current != "usersMoradores") listNameCollection.Add(current);
                            }
                        }
                    }

                    return listNameCollection;
                }

            }

            else
            {
                return Unauthorized();
            }

        }
        public dynamic GetInfoUser(HttpRequest request)
        {


            if (_userService.ValidateToken(request))
            {
                JObject jsonClaim = _userService.UnGenereteToken(request);
                IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(jsonClaim["database"].ToString());
                IMongoCollection<UserAdm> _users = _newDatabase.GetCollection<UserAdm>("users");
                UserAdm _user = _users.Find(_user => _user.id == jsonClaim["objectId"].ToString()).ToList()[0];
                _user.password = "Não veja";
                return Ok(_user);
            }
            else
            {
                return Unauthorized("Token Inválido");
            }
        }
        public dynamic LoginCondominio(UserGeneric user, HttpRequest request)
        {
            try
            {
                //puxar primeiro qual nome do condominio equivale o email e se existe
                IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase("userscondominio");
                IMongoCollection<UserReferencia> _users2 = _newDatabase.GetCollection<UserReferencia>("users");
                UserReferencia userref = _users2.Find(userref => userref.email == user.email).ToList()[0];

                _newDatabase = _clientMongoDb.GetDatabase(userref.databaseName);


                if (userref.role == "Administrator")
                {
                    IMongoCollection<UserAdm> _users = _newDatabase.GetCollection<UserAdm>("usersAdm");
                    UserAdm _user = _users.Find(_user => _user.email == user.email).FirstOrDefault();
                    if (!_userService.VerificarSenha(_users, _user, user.password))
                        return Unauthorized();
                    string _tokenUser = _userService.GenerateToken(_user, _timeExpiredTokenLogin);
                    return Ok(new { token = _tokenUser });
                }
                else if (userref.role == "Porteiro")
                {
                    if (_userService.verificaPagamento(userref.nameCondominio))
                    {
                        IMongoCollection<UserPorteiro> _users = _newDatabase.GetCollection<UserPorteiro>("usersPorteiros");
                        UserPorteiro _user = _users.Find(_user => _user.email == user.email).FirstOrDefault();
                        if (!_userService.VerificarSenha(_users, _user, user.password))
                            return Unauthorized();
                        string _tokenUser = _userService.GenerateToken(_user, _timeExpiredTokenLogin);
                        return Ok(new { token = _tokenUser });
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }
                }
                return BadRequest();

            }
            catch (SystemException)
            {
                return Unauthorized();
            }

        }
        public dynamic LoginMorador(UserGenericLogin user, HttpRequest request)
        {
            try
            {
                if (VerificarDatabaseExist(user.nameCondominio))
                {
                    if (_userService.verificaPagamento(user.nameCondominio))
                    {
                        IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(user.nameCondominio));

                        IMongoCollection<UserMorador> _users = _newDatabase.GetCollection<UserMorador>("usersMoradores");
                        UserMorador _user = _users.Find(_user => _user.email == user.email).FirstOrDefault();
                        if (!_userService.VerificarSenha(_users, _user, user.password))
                            return Unauthorized();
                        string _tokenUser = _userService.GenerateToken(_user, _timeExpiredTokenLogin);
                        return Ok(new { token = _tokenUser });
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }
                }
                else
                {
                    return NotFound(user.nameCondominio + " Não existe.");
                }

            }
            catch (SystemException)
            {
                return Unauthorized();
            }

        }
        public dynamic RegisterCondominio(UserAdm user, HttpRequest request)
        {
            try
            {
                if (!_userService.EmailExist(user, _clientMongoDb))
                {

                    if (VerificarDatabaseExist(user.nameCondominio))
                    {
                        return Conflict("Esse nome de condominio já esta cadastrado, tente outro nome");
                    }
                    else
                    {
                        if (cnpjValidador.IsValid(user.cnpj))
                        {
                            //criando ou pegando o banco de dados com o nome do condiminio
                            IMongoDatabase _newDatabase = _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(user.nameCondominio));
                            //usando o banco para criar as collections
                            _newDatabase.CreateCollection("usersAdm");
                            _newDatabase.CreateCollection("usersPorteiros");
                            _newDatabase.CreateCollection("usersMoradores");
                            _newDatabase.CreateCollection("configApp");
                            _newDatabase.CreateCollection("avisos");
                            _newDatabase.CreateCollection("historicoPagamento");

                            BsonDocument novoAdm = _userService.RetornaUserAdm(user);
                            _newDatabase.GetCollection<BsonDocument>("usersAdm").InsertOne(novoAdm);

                            _newDatabase = _clientMongoDb.GetDatabase("userscondominio");
                            if (_newDatabase.GetCollection<BsonDocument>("users") == null)
                            {
                                _newDatabase.CreateCollection("users");
                                _newDatabase.GetCollection<BsonDocument>("users").InsertOne(_userService.RetornaUserRef(user));
                            }
                            else
                            {
                                _newDatabase.GetCollection<BsonDocument>("users").InsertOne(_userService.RetornaUserRef(user));
                            }
                            user.role = "Administrator";
                            user.id = novoAdm["_id"].ToString();
                            _userService.EmailConfimacao(user);
                            return Ok("Condominio " + user.nameCondominio + " cadastrado com sucesso!");
                        }
                        else
                        {
                            return StatusCode(406, "Cnpj invalido");
                        }

                    }

                }
                else
                {
                    return Conflict("Email já existe");

                }

            }
            catch (System.Exception e)
            {
                Console.Write(e);
                return "Não foi possivel realizar o cadastro, tente novamente mais tarde.";

            }

        }
        public dynamic RegisterPorteiro(UserPorteiro user, HttpRequest request)
        {

            JObject jsonClaim = _userService.UnGenereteToken(request);
            user.nameCondominio = jsonClaim["nameCondominio"].ToString();
            if (!_userService.EmailExist(user, _clientMongoDb))
            {
                if (_userService.verificaEmailConfirmado(user.nameCondominio))
                {
                    if (_userService.verificaPagamento(user.nameCondominio))
                    {
                        _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(user.nameCondominio)).GetCollection<BsonDocument>("usersPorteiros").InsertOne(_userService.RetornaUserPorteiro(user));
                        _clientMongoDb.GetDatabase("userscondominio").GetCollection<BsonDocument>("users").InsertOne(_userService.RetornaUserRef(user));
                        return Ok(user.nome + " Cadastrado com sucesso.");
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }

                }
                else
                {
                    return Unauthorized("Email não confirmado");
                }
            }
            else
            {
                return Conflict("Email Já Cadastrado");
            }

        }
        public dynamic RegisterMorador(UserMorador user, HttpRequest request)
        {

            JObject jsonClaim = _userService.UnGenereteToken(request);
            user.nameCondominio = jsonClaim["nameCondominio"].ToString();
            if (!_userService.EmailExist(user, _clientMongoDb))
            {
                if (_userService.verificaEmailConfirmado(user.nameCondominio))
                {
                    if (_userService.verificaPagamento(user.nameCondominio))
                    {
                        _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(user.nameCondominio)).GetCollection<BsonDocument>("usersMoradores").InsertOne(_userService.RetornaUserMorador(user));
                        return Ok("Morador " + user.nome + " Cadastrado com sucesso.");
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }

                }
                else
                {
                    return Unauthorized("Email não confirmado");
                }

            }
            else
            {
                return Conflict("Email Já Cadastrado");
            }

        }
        public dynamic CadastroComunicado(Aviso texto, HttpRequest request)
        {
            try
            {
                JObject jsonClaim = _userService.UnGenereteToken(request);
                string nameCondominio = jsonClaim["nameCondominio"].ToString();
                if (_userService.verificaEmailConfirmado(nameCondominio))
                {
                    if (_userService.verificaPagamento(nameCondominio))
                    {
                        _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(nameCondominio)).GetCollection<BsonDocument>("avisos").InsertOne(objectsService.RetornaAviso(texto));
                        return Ok("Aviso " + texto.titulo + " cadastrado com Sucesso");
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }


                }

                else
                {


                    return Unauthorized("Email não confirmado");
                }
            }
            catch (System.Exception)
            {
                return Conflict();
            }

        }
        public dynamic CriarAgendamento(Agendamento agend, HttpRequest request)
        {
            try
            {
                JObject jsonClaim = _userService.UnGenereteToken(request);
                string nameCondominio = jsonClaim["nameCondominio"].ToString();
                string nameCollection = "configApp";
                if (_userService.verificaEmailConfirmado(nameCondominio))
                {
                    if (_userService.verificaPagamento(nameCondominio))
                    {
                        if (!verificarAgendamentosExist(nameCondominio, agend.itemNome, nameCollection))
                        {

                            IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                            db.GetCollection<BsonDocument>(nameCollection).InsertOne(objectsService.RetornaAgendamento(agend));
                            db.CreateCollection(_userService.RemoverCaracterEspecialDeixarEspaco(agend.itemNome));
                            return Ok(agend.itemNome + " para agendamentos cadastrado com Sucesso");

                        }
                        else
                        {
                            return Conflict(agend.itemNome + " Já está cadastrado !");
                        }
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }

                }
                else
                {
                    return Unauthorized("Email não confirmado");
                }

            }
            catch (System.Exception e)
            {
                Console.Write(e);
                return Conflict();
            }

        }
        public dynamic CadastrarAgendamento(CriacaoAgendamento agend, String name, HttpRequest request)
        {
            try
            {
                string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
                if (_userService.verificaEmailConfirmado(nameCondominio))
                {
                    if (_userService.verificaPagamento(nameCondominio))
                    {
                        IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                        db.GetCollection<BsonDocument>(_userService.RemoverCaracterEspecialDeixarEspaco(name)).InsertOne(objectsService.RetornaCriacaoAgendamento(agend, request));
                        return Ok("Agendado em " + name + " para " + agend.dateAgendamento + ", Sucesso !");
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }
                }
                else
                {
                    return Unauthorized("Email não confirmado");
                }

            }
            catch (System.Exception e)
            {
                Console.Write(e);
                return Conflict();
            }

        }
        public dynamic EditarAgendamento(Agendamento agend, HttpRequest request)
        {
            try
            {
                string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
                string nameCollection = "configApp";
                if (_userService.verificaEmailConfirmado(nameCondominio))
                {
                    if (_userService.verificaPagamento(nameCondominio))
                    {

                        if (verificarAgendamentosExist(nameCondominio, agend.itemNome, nameCollection))
                        {
                            IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                            BsonDocument old = _userService.GetBson(nameCondominio, agend.itemNome, nameCollection);
                            BsonDocument novo = objectsService.RetornaAgendamento(agend);
                            novo["_id"] = old["_id"];
                            db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                            return Ok("Agendado em " + agend.itemNome + " alterado com Sucesso !");
                        }
                        else
                        {
                            return Conflict(agend.itemNome + " não exite.");
                        }
                    }
                    else
                    {
                        return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                    }
                }
                else
                {
                    return Unauthorized("Email não confirmado");
                }


            }
            catch (System.Exception e)
            {
                Console.Write(e);
                return Conflict();
            }

        }
        public dynamic EditarSenha(String token, string senha, HttpRequest request)
        {
            request.Headers["Authorization"] = "1 " + token;
            //password para hash
            string _passwordSHA256 = _userService.passwordToHash(senha);
            string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
            string role = _userService.UnGenereteToken(request)["role"].ToString();
            string id = _userService.UnGenereteToken(request)["objectId"].ToString();
            //pego o database  
            IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
            if (_userService.ValidateToken(request))
            {
                if (role == "Administrator")
                {
                    string nameCollection = "usersAdm";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    novo["alg"] = UserGeneric.Bcrypt12Algorithm;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");

                }
                else if (role == "Porteiro")
                {
                    string nameCollection = "usersPorteiros";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    novo["alg"] = UserGeneric.Bcrypt12Algorithm;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");
                }
                else if (role == "Morador")
                {
                    string nameCollection = "usersMoradores";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    novo["alg"] = UserGeneric.Bcrypt12Algorithm;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");
                }
                else
                {
                    return Unauthorized();
                }

            }
            else
            {
                return Unauthorized();
            }
        }
        public dynamic AlterarSenha(string senha, HttpRequest request)
        {
            //password para hash
            string _passwordSHA256 = _userService.passwordToHash(senha);
            string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
            string role = _userService.UnGenereteToken(request)["role"].ToString();
            string id = _userService.UnGenereteToken(request)["objectId"].ToString();
            //pego o database  
            IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
            if (_userService.ValidateToken(request))
            {

                if (role == "Administrator")
                {
                    string nameCollection = "usersAdm";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    novo["alg"] = UserGeneric.Bcrypt12Algorithm;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");

                }
                else if (role == "Porteiro")
                {
                    string nameCollection = "usersPorteiros";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    novo["alg"] = UserGeneric.Bcrypt12Algorithm;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");
                }
                else if (role == "Morador")
                {
                    string nameCollection = "usersMoradores";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    novo["alg"] = UserGeneric.Bcrypt12Algorithm;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");
                }
                else
                {
                    return Unauthorized();
                }

            }
            else
            {
                return Unauthorized();
            }
        }
        public dynamic ConfirmacaoEmail(String token, HttpRequest request)
        {
            request.Headers["Authorization"] = "1 " + _userService.RemoverBarraToken(token);

            if (_userService.ValidateToken(request))
            {
                string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
                string role = _userService.UnGenereteToken(request)["role"].ToString();
                string id = _userService.UnGenereteToken(request)["objectId"].ToString();
                //pego o database  
                IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                if (role == "Administrator")
                {
                    string nameCollection = "usersAdm";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["verificado"] = true;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Email Confirmado");

                }
                /*else if (role == "Porteiro")
                {
                    string nameCollection = "usersPorteiros";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");
                }
                else if (role == "Morador")
                {
                    string nameCollection = "usersMoradores";
                    BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                    BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                    novo["password"] = _passwordSHA256;
                    //pego a colection                         faço a alteração
                    db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                    return Ok("Senha Alterada");
                } */
                else
                {
                    return Unauthorized();
                }

            }
            else
            {
                return Unauthorized();
            }
        }
        public dynamic EmailNaoConfirmado(ConfirmacaoEmail confirm, HttpRequest request)
        {

            if (VerificarDatabaseExist(confirm.nameCondominio))
            {
                IMongoDatabase db = _clientMongoDb.GetDatabase(confirm.nameCondominio);

                try
                {
                    string nameCollection = "usersAdm";
                    IMongoCollection<UserAdm> _users = db.GetCollection<UserAdm>(nameCollection);
                    UserAdm _user = _users.Find(_user => _user.email == confirm.email).ToList()[0];
                    _userService.EmailConfimacao(_user);
                    return Ok("Enviamos novamente um Email de confirmação, (Valido por 30 minutos)");
                }
                catch
                {
                    return NotFound("Usuario não encontrado");
                }

            }
            else
            {
                return NotFound("Condominio não encontrado");
            }

        }
        public dynamic RedefinirSenha(RedefinirSenha red, HttpRequest request)
        {
            if (VerificarDatabaseExist(red.nameCondominio))
            {
                IMongoDatabase db = _clientMongoDb.GetDatabase(red.nameCondominio);
                if (red.role == "Administrator")
                {
                    try
                    {
                        string nameCollection = "usersAdm";
                        IMongoCollection<UserAdm> _users = db.GetCollection<UserAdm>(nameCollection);
                        UserAdm _user = _users.Find(_user => _user.email == red.email).ToList()[0];
                        string _tokenUser = _userService.GenerateToken(_user, _timeExpiredTokenLogin);
                        _userService.EmailDeRedefinicaoDeSenha(_user);
                        return Ok("Verique seu email (Valido por 30 minutos)");
                    }
                    catch
                    {
                        return NotFound("Usuario não encontrado");
                    }
                }
                else if (red.role == "Porteiro")
                {
                    try
                    {
                        string nameCollection = "usersPorteiros";
                        IMongoCollection<UserAdm> _users = db.GetCollection<UserAdm>(nameCollection);
                        UserAdm _user = _users.Find(_user => _user.email == red.email).ToList()[0];
                        string _tokenUser = _userService.GenerateToken(_user, _timeExpiredTokenLogin);
                        _userService.EmailDeRedefinicaoDeSenha(_user);
                        return Ok("Verique seu email (Valido por 30 minutos)");
                    }
                    catch
                    {
                        return NotFound("Usuario não encontrado");
                    }
                }
                else if (red.role == "Morador")
                {
                    try
                    {
                        string nameCollection = "usersMoradores";
                        IMongoCollection<UserAdm> _users = db.GetCollection<UserAdm>(nameCollection);
                        UserAdm _user = _users.Find(_user => _user.email == red.email).ToList()[0];
                        string _tokenUser = _userService.GenerateToken(_user, _timeExpiredTokenLogin);
                        _userService.EmailDeRedefinicaoDeSenha(_user);
                        return Ok("Verique seu email (Valido por 30 minutos)");
                    }
                    catch
                    {
                        return NotFound("Usuario não encontrado");
                    }
                }
                else
                {
                    return NotFound("Usuario não encontrado");
                }
            }
            else
            {
                return NotFound("Condominio não encontrado");
            }

        }
        public dynamic ListaItensAgendamentos(ObjectBase obj, HttpRequest request)
        {
            try
            {

                if (_userService.ValidateToken(request) & _userService.UnGenereteToken(request)["role"].ToString() != "Morador")
                {
                    string nameCondominio = _userService.RemoverCaracterEspecial(_userService.UnGenereteToken(request)["nameCondominio"].ToString());
                    if (_userService.verificaEmailConfirmado(nameCondominio))
                    {
                        if (_userService.verificaPagamento(nameCondominio))
                        {

                            List<CriacaoAgendamento> list = new List<CriacaoAgendamento>();
                            IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                            list = db.GetCollection<CriacaoAgendamento>(obj.itemNome).Find(_ => true).ToList();
                            return RetornaListaComUserName(list, nameCondominio);
                        }
                        else
                        {
                            return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                        }
                    }
                    else
                    {
                        return Unauthorized("Email não confirmado");
                    }

                }
                else
                {
                    return Unauthorized();
                }

            }
            catch (System.Exception)
            {
                return Conflict();
            }

        }
        public dynamic EnviarFoto(byte[] foto, HttpRequest request)
        {
            try
            {

                if (_userService.ValidateToken(request))
                {
                    string role = _userService.UnGenereteToken(request)["role"].ToString();
                    string id = _userService.UnGenereteToken(request)["_id"].ToString();
                    string nameCondominio = _userService.RemoverCaracterEspecial(_userService.UnGenereteToken(request)["nameCondominio"].ToString());
                     if (_userService.verificaEmailConfirmado(nameCondominio))
                    {
                        if (_userService.verificaPagamento(nameCondominio))
                        {
                            if (role == "Administrator")
                            {
                                string nameCollection = "usersAdm";
                                IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                                BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                                BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                                novo["image"] = foto;
                                db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                                return Ok("Foto alterada com Sucesso !");
                            }
                            else if (role == "Porteiro")
                            {

                                string nameCollection = "usersPorteiros";
                                IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                                BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                                BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                                novo["image"] = foto;
                                db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                                return Ok("Foto alterada com Sucesso !");
                            }
                            else if (role == "Morador")
                            {
                                string nameCollection = "usersMoradores";
                                IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                                BsonDocument old = _userService.GetBson(nameCondominio, id, nameCollection);
                                BsonDocument novo = _userService.GetBson(nameCondominio, id, nameCollection);
                                novo["image"] = foto;
                                db.GetCollection<BsonDocument>(nameCollection).ReplaceOne(old, novo);
                                return Ok("Foto alterada com Sucesso !");
                            }
                        }
                        else
                        {
                            return Unauthorized("Pagamento não localizado para o mês, converse com o seu Sindico.");
                        }
                    }
                    else
                    {
                        return Unauthorized("Email não confirmado");
                    }

                }

                return Unauthorized();

            }
            catch (System.Exception)
            {
                return Conflict("erro");
            }
        }
        public Boolean VerificarDatabaseExist(String nameDatabase)
        {
            try
            {
                if (_clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(nameDatabase)).ListCollections().ToList().Count > 0)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        public Boolean verificarAgendamentosExist(String nameDatabase, String nameitem, String nameCollection)
        {
            try
            {
                if (_userService.GetBson(nameDatabase, nameitem, nameCollection) != null)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }
        public dynamic GetAgendamento(HttpRequest request)
        {
            string role = _userService.UnGenereteToken(request)["role"].ToString();
            if ((role == "Administrator" || role == "Porteiro" || role == "Morador") & _userService.ValidateToken(request))
            {
                string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
                List<Aviso> list = new List<Aviso>();
                IMongoDatabase db = _clientMongoDb.GetDatabase(nameCondominio);
                return db.GetCollection<Aviso>("avisos").Find(_ => true).ToList();
            }
            else
            {
                return Unauthorized();
            }
        }
        public dynamic GetConfigAgendamentos(ObjectBase obj, HttpRequest request)
        {

            string role = _userService.UnGenereteToken(request)["role"].ToString();
            if ((role == "Administrator" || role == "Porteiro" || role == "Morador") & _userService.ValidateToken(request))
            {
                string nameCondominio = _userService.UnGenereteToken(request)["nameCondominio"].ToString();
                IMongoDatabase db = _clientMongoDb.GetDatabase(_userService.RemoverCaracterEspecial(nameCondominio));
                IMongoCollection<Agendamento> config = db.GetCollection<Agendamento>("configApp");
                Agendamento agend = config.Find(agend => agend.itemNome == _userService.RemoverCaracterEspecialDeixarEspaco(obj.itemNome)).ToList()[0];
                agend.id = "";
                return agend;
            }
            else
            {
                return Unauthorized();
            }
        }
        public List<CriacaoAgendamento> RetornaListaComUserName(List<CriacaoAgendamento> agend, String nameDatabase)
        {

            IMongoCollection<UserMorador> user = _clientMongoDb.GetDatabase(nameDatabase).GetCollection<UserMorador>("usersMoradores");
            foreach (var item in agend)
            {
                item.id = "";
                Console.Write(item.idUser);
                UserMorador user1 = user.Find(user1 => user1.id == item.idUser).ToList()[0];
                item.idUser = user1.nome;

            }


            return agend;


        }


    }
}
