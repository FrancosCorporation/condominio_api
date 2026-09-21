# condominio_api

## 🐳 Instalação e Execução (Docker) — recomendado

### Pré-requisitos
- [Docker](https://docs.docker.com/get-docker/) + Docker Compose

### Rodar com Docker
```bash
docker compose up --build
```
```bash
docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:8.0 dotnet run
```
Env vars: CONDOMINIO_JWT_SECRET, JUNO_RESOURCE_TOKEN, JUNO_AUTHORIZATION, JUNO_PLAN_ID

### Sem Docker (local)
```bash
# Requer .NET SDK
export CONDOMINIO_JWT_SECRET='sua-chave'
dotnet build
dotnet run
```

API REST multi-tenant para gestão de condomínios: cadastro de condomínios, porteiros e moradores, comunicados, agendamento de áreas comuns, confirmação de e-mail e cobrança recorrente da assinatura via Juno.

![C#](https://img.shields.io/badge/C%23-239120?style=flat-square&logo=csharp&logoColor=white)
![.NET](https://img.shields.io/badge/.NET-5.0-512BD4?style=flat-square&logo=dotnet&logoColor=white)
![MongoDB](https://img.shields.io/badge/MongoDB-47A248?style=flat-square&logo=mongodb&logoColor=white)
![License](https://img.shields.io/badge/license-MIT-green?style=flat-square)
![Status](https://img.shields.io/badge/status-em%20desenvolvimento-yellow?style=flat-square)

## Sobre

Backend de um sistema de automação condominial, pensado para síndicos/administradoras e aplicativos de morador e porteiro. O projeto resolve o dia a dia do condomínio em um único serviço:

- **Multi-tenant por banco de dados**: cada condomínio cadastrado ganha um banco próprio no MongoDB, com coleções isoladas para administrador, porteiros, moradores, avisos, configuração de agendamentos e histórico de pagamento.
- **Autenticação JWT com papéis** (`Administrator`, `Porteiro`, `Morador`) controlando o acesso a cada rota.
- **Monetização por assinatura**: integração com o gateway de pagamentos **Juno** (tokenização de cartão, criação de assinatura e consulta de cobranças) com um *hosted service* que verifica periodicamente se a mensalidade do condomínio está em dia.

## Funcionalidades

Comprovadas pelo código em `Controllers/` e `Services/`:

- Cadastro de condomínio (`POST /api/cadastroCondominio`) com validação de CNPJ, criação automática do banco/coleções e envio de e-mail de confirmação.
- Login de administrador (`POST /api/loginCondominio`) e de morador (`POST /app/loginMorador`) com emissão de token JWT.
- Cadastro de porteiro (`POST /api/cadastroPorteiro`) e de morador (`POST /api/cadastroMorador`) restritos ao papel `Administrator`.
- Listagem dos condomínios cadastrados (`GET /app/listacondominios`).
- Comunicados: cadastro pelo admin (`POST /api/cadastroComunicado`) e leitura por qualquer papel autenticado (`GET /api/comunicados`).
- Agendamento de áreas comuns: criação/edição da configuração pelo admin (`POST /api/criarAgendamento`, `PUT /api/editAgendamento`), solicitação pelo morador (`POST /app/agendar`), listagens e consulta de configuração (`GET /api/listaAgendamentos`, `GET /api/listaItensAgendamentos`, `GET /api/configAgendamentos`).
- Edição da foto de perfil do usuário autenticado (`POST /api/editFoto`), com verificação de e-mail confirmado e pagamento em dia.
- Confirmação de e-mail por token (`GET /api/confirmacaoEmail`, `POST /api/EmailNaoConfirmado`) e redefinição/alteração de senha (`POST /api/esqueciMinhaSenha`, `GET /api/recuperarSenhaCondominio`, `PUT /api/alterarSenha`).
- Assinatura e cobrança: `POST /subscription` tokeniza o cartão e cria a assinatura na Juno; `GET /subscription/Consult` lista as cobranças; `Services/CheckPayment.cs` roda como `IHostedService` e grava o histórico de pagamento, atualizando o campo `isPayment` do condomínio.
- Senhas armazenadas com hash SHA-256 e envio de e-mails transacionais via SMTP.

## Stack

- **Linguagem/framework**: C# com ASP.NET Core 5.0 (Web API)
- **Banco de dados**: MongoDB (`MongoDB.Driver` 2.12.1), um banco por condomínio
- **Autenticação**: JWT (`Microsoft.AspNetCore.Authentication.JwtBearer`), com pacotes de Identity/OpenIdConnect referenciados
- **Pagamentos**: Juno — `RestSharp` 106.11.7 + `Newtonsoft.Json`
- **Validação**: `Atividio.Validadores.Cnpj`
- **E-mail**: `System.Net.Mail` (SMTP Gmail)

## Como rodar

Requer configuração de ambiente. Não há `appsettings.json` nem `Settings.cs` versionados (ambos estão no `.gitignore`, pois guardam credenciais), então o projeto **não compila sem criá-los** na raiz:

1. Instale o [.NET 5 SDK](https://dotnet.microsoft.com/download/dotnet/5.0).
2. Crie o arquivo `Settings.cs` com a classe estática `Settings` usada em `Startup.cs`, `Services/Payment.cs`, `Services/User.cs` e `Services/CheckPayment.cs`:

   ```csharp
   namespace condominio_api
   {
       public static class Settings
       {
           public static string Secret = "<chave-jwt>";
           public static string Authorization = "<client_credentials-base64-da-juno>";
           public static string Token = "<resource-token-da-juno>";
           public static string PlanId = "<id-do-plano-juno>";
       }
   }
   ```

3. Crie o `appsettings.json` com a seção lida por `Models/CondominioDatabaseSetting.cs`:

   ```json
   {
     "CondominioDatabaseSetting": {
       "ConnectionString": "mongodb://usuario:senha@host:27017",
       "DatabaseName": "Condominios"
     }
   }
   ```

4. Restaure e execute:

   ```bash
   dotnet restore
   dotnet run
   ```

Observações: o serviço espera um MongoDB acessível; a integração de pagamento aponta para o sandbox da Juno (`https://sandbox.boletobancario.com`) e o `CheckPayment` consulta cobranças a cada 2 segundos. As URLs de e-mail e o caminho do template HTML (`Templates/confirmed.html`) estão fixos no código.

## Estrutura do projeto

```
condominio_api/
├── Controllers/            # Rotas HTTP: app do morador, painel do síndico e assinatura
│   ├── CondominioApp.cs
│   ├── CondominioSite.cs
│   └── Payment.cs
├── DependencyServices/     # Interfaces de injeção de dependência (IUserService, IPaymentService)
├── Models/                 # Entidades do domínio e modelos de resposta da Juno (Models/Juno)
├── Services/               # Regras de negócio, acesso ao Mongo, e-mail e pagamentos
│   ├── CheckPayment.cs     # IHostedService que sincroniza o status de pagamento
│   ├── Condominio.cs
│   ├── Payment.cs
│   └── User.cs
├── Templates/              # Template HTML do e-mail de confirmação
├── Properties/             # launchSettings.json
├── Program.cs
├── Startup.cs
└── condominio_api.csproj
```

## Licença

Distribuído sob a licença MIT. Veja [LICENSE](LICENSE).
