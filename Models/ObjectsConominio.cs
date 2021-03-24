using System.ComponentModel.DataAnnotations;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;

namespace condominioApi.Models
{

    public class Aviso
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }
        [Required]
        public string titulo { get; set; }
        [Required]
        public string mensagem { get; set; }
        public int datacreate { get; set; }
    }
    public class ObjectBase
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }
        [Required]
        public string itemNome { get; set; }
        public int datacreate { get; set; }
    }
    public class CriacaoAgendamento
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string id { get; set; }

        [BsonRepresentation(BsonType.ObjectId)]
        public string idUser { get; set; }
        [Required]
        public int dateAgendamento { get; set; }
        public int datacreate { get; set; }
    }
    public class Agendamento : ObjectBase
    {
        public BsonBoolean ativo { get; set; }
        [Required]
        public string horaInicio { get; set; }
        [Required]
        public string horaFim { get; set; }
        [Required]
        public string tempoUtilizacao { get; set; }
        [Required]
        public int qntPessoas { get; set; }
        [Required]
        public string diasSemana { get; set; }
        public string descricao { get; set; }

    }

}