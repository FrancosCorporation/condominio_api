using condominio_api.Models;
using MongoDB.Bson;
using System;
using Microsoft.AspNetCore.Http;
using MongoDB.Driver;
using Newtonsoft.Json.Linq;

namespace condominio_api.DependencyService
{
    public interface IUserService
    {
        BsonDocument RetornaUserAdm(UserAdm user);
        BsonDocument RetornaUserRef(UserGenericLogin user);
        BsonDocument RetornaUserPorteiro(UserPorteiro user);
        BsonDocument RetornaUserMorador(UserMorador user);
        Boolean EmailExist(UserGenericLogin user, IMongoClient database);
        string passwordToHash(string password);
        bool VerificarSenha<TUser>(IMongoCollection<TUser> users, TUser user, string password)
            where TUser : UserGeneric;
        string GenerateToken(UserGenericLogin user, double horas);
        Boolean ValidateToken(HttpRequest request);
        JObject UnGenereteToken(HttpRequest request);
        string RemoverCaracterEspecial(string str);
        string RemoverCaracterEspecialDeixarEspaco(string str);
        string RemoverBarraToken(string str);
        void SendEmail(ConstrucaoEmail email);
        void EmailConfimacao(UserGenericLogin user);
        void EmailDeRedefinicaoDeSenha(UserGenericLogin user);
        UserAdm RetornaUserAdmPorId(string nameCondominio, string id);
        BsonDocument GetBson(String nameDatabase, String nameitem, String nameCollection);
        void GravaUserAdm(UserAdm user);
        bool verificaEmailConfirmado(string nameCondominio);
        bool verificaPagamento(string nameCondominio);
    }
}
