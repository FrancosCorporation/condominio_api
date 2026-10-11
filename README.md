# condominio_api

API REST para gestão multi-tenant de condomínios. Cada condomínio usa um database próprio no
MongoDB. O serviço oferece cadastro e autenticação de administradores, porteiros e moradores,
comunicados, agendamento de áreas comuns e cobrança recorrente via Juno.

## Requisitos

- .NET SDK 10
- MongoDB acessível pela API
- Credenciais Juno para fluxos de assinatura
- SMTP configurado para confirmação e redefinição de senha

## Configuração

Copie .env.example para .env e preencha os valores. O .NET não carrega .env automaticamente:
exporte as variáveis no processo ou configure-as no ambiente do serviço/container. Não versione
.env nem inclua credenciais em appsettings*.json.

As opções MongoDB também podem ser fornecidas pelo sistema de configuração do ASP.NET Core:

~~~text
CondominioDatabaseSetting__ConnectionString=mongodb://usuario:senha@host:27017
CondominioDatabaseSetting__DatabaseName=Condominios
~~~

CONDOMINIO_JWT_SECRET deve conter pelo menos 32 bytes UTF-8. Em produção, configure também
APP_BASE_URL, ALLOWED_ORIGINS, as credenciais SMTP e as credenciais e URL de produção da Juno.
A aplicação rejeita configuração incompleta ou uma URL da Juno que ainda aponte para sandbox.

ALLOWED_ORIGINS aceita origens HTTP/HTTPS separadas por ponto e vírgula, sem caminhos, por exemplo:
https://painel.exemplo.com;https://app.exemplo.com. Não use *.

## Executar

~~~bash
dotnet restore
dotnet run --project condominio_api.csproj
~~~

Para um build de publicação:

~~~bash
dotnet publish condominio_api.csproj --configuration Release --output publish
~~~

Os templates de e-mail são copiados para a saída de build e publicação. Este repositório não contém
Dockerfile nem arquivo Compose.

## Operação

- PAYMENT_CHECK_MINUTES controla a conciliação Juno; o padrão e o mínimo são 15 minutos, e o máximo
  é 1440. O primeiro ciclo começa logo após a inicialização.
- O ciclo ignora o acesso à Juno quando não há assinatura cadastrada, consulta as cobranças uma vez
  por ciclo e registra falhas no log. Uma falha não cancela o próximo ciclo.
- JUNO_BASE_URL seleciona o ambiente da integração. Desenvolvimento usa sandbox por padrão;
  produção exige uma URL explícita e rejeita sandbox.
- Os links de confirmação de e-mail e redefinição de senha expiram em 30 minutos e só podem ser
  usados uma vez.
- Senhas BCrypt12 são usadas para novos cadastros. Senhas legadas SHA-256 são atualizadas para
  BCrypt no próximo login válido.

## Verificação e CI

O workflow GitHub Actions restaura dependências, compila em Release com warnings como erros e falha
se a auditoria NuGet encontrar dependências vulneráveis. Atualmente o repositório não possui projeto
de testes automatizados.

## Segurança

Consulte SECURITY.md para o processo de reporte e as regras de isolamento e cobrança.

## Licença

MIT. Consulte LICENSE.
