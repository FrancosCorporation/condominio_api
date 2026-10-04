# MELHORIAS — condominio_api

> **Gerado por análise de código em 2026-10-02** · Stack: .NET 5 (ASP.NET Core, JWT Bearer, MongoDB, Juno)
> Branch `master` · base `963e001` · 2.485 LOC · **0 testes** · 27 arquivos de código
>
> **Este arquivo é um plano de execução.** Cada item tem ID, `arquivo:linha`, mudança exata,
> critério de aceite e comando de verificação.
>
> **⚠️ Este projeto processa pagamento (Juno) e e-mail transacional.** Os itens P0 são os que podem
> vazar credencial, quebrar cobrança ou expor dados de outro condomínio.

---

## 0. Como usar este documento

1. Execute na ordem **P0 → P1 → P2 → P3**, respeitando as ondas da §8.
2. Ao terminar um item: marque `- [x]`, rode o **Verificação**, comite `fix(<ID>): descrição`.
3. **Não reescreva o caminho Juno.** `Services/Payment.cs` + `Services/CheckPayment.cs` formam um
   fluxo testado na prática; os itens aqui são de **contorno** (exposição, frequência, isolamento).
4. **`Config/Settings.cs` é o único ponto de segredo.** Toda correção de credencial passa por ele —
   ver `SEC-01`, `SEC-05` e `SEC-08`. Não espalhe `Environment.GetEnvironmentVariable` pelo código.
5. **Idioma:** comentários/respostas em português (padrão do repo); commits em inglês com
   prefixo `fix:`/`feat:`/`docs:`.

---

## 1. Diagnóstico executivo

API de gestão de condomínios com integração ao gateway Juno: cobra assinatura via cartão, verifica
pagamentos num timer de fundo (`CheckPayment`) e isola cada condomínio num database Mongo próprio.
O modelo multi-tenant é o mesmo da `api_authentication` irmã — e sofre dos mesmos males, mais alguns
próprios.

**O que está bem (não refaça):**

| Item | Evidência |
|---|---|
| Segredos Juno lidos de env | `Config/Settings.cs:23-30` (`JUNO_RESOURCE_TOKEN`, `JUNO_AUTHORIZATION`, `JUNO_PLAN_ID`) |
| Token de acesso Juno com cache temporal | `Payment.cs:26` (`dateTimeGenerateAccessToken`) + `:30-34` (`isTokenExpiration`, 1 h) |
| Queries Mongo parametrizadas (sem concatenação) | `Builders<UserAdm>.Update.Set(...)` em `CheckPayment.cs:38` |
| `appsettings.json` no `.gitignore` | (confirmado no achado de 21/09) |
| Claims com fronteira de tenant | `User.cs:178-183` (`objectId`, `nameCondominio`, `role`) |

**O que está quebrado:**

1. **Senha do Gmail escrita no código e versionada** (`Services/User.cs:297`). Não é fallback — é
   literal ativo no caminho de e-mail transacional, e o arquivo está no índice do git.
2. **Timer de pagamento a cada 2 segundos** (`Services/CheckPayment.cs:32`). Cada ciclo lista todos
   os databases do Mongo e consulta o gateway Juno — é o equivalente a um DDoS próprio contra o
   provedor de pagamento, executado 43.200 vezes por dia.
3. **`changeIsPayment` marca TODO o banco com o predicado `user => true`** (`CheckPayment.cs:36-40`).
   Em cenário multi-tenant, um pagamento de um condomínio ativa/desativa **todos os outros**.
4. **Caminho absoluto Windows** (`Services/User.cs:329,345`) — fora do Windows, o e-mail quebra.
5. **CORS `AllowAnyOrigin`** (`Startup.cs:32`) com o argumento de política **divergente** do que o
   pipeline usa (`Startup.cs:74` passa `MyAllowSpecificOrigins`, que nunca foi registrada).

O item 1 é P0 de vazamento; os itens 2 e 3 são os P0 de dinheiro.

---

## 2. Tabela de prioridades

| ID | Título | Sev | Arquivo | Depende de |
|---|---|---|---|---|
| SEC-01 | Senha do Gmail versionada no código | **P0** | `Services/User.cs:297` | — |
| SEC-02 | Timer de cobrança a cada 2 segundos (self-DoS no Juno) | **P0** | `Services/CheckPayment.cs:32` | — |
| SEC-03 | `changeIsPayment` atualiza todos os tenants de uma vez | **P0** | `Services/CheckPayment.cs:34-40` | — |
| SEC-04 | SHA-256 cru (sem sal) no lugar de bcrypt | **P0** | `Services/User.cs:159-172` | — |
| SEC-05 | Fallback JWT previsível em `DevSecretFallback` | **P1** | `Config/Settings.cs:17` | — |
| SEC-06 | CORS `AllowAnyOrigin` + política nunca registrada | **P1** | `Startup.cs:32,74` | — |
| SEC-07 | `ValidateToken` estoura sem header (`Split[1]`) | **P1** | `Services/User.cs:196` | — |
| SEC-08 | Chave SMTP e URL do Juno impossíveis de rotacionar sem deploy | **P1** | `Services/User.cs:296-297`, `Services/Payment.cs:28` | SEC-01 |
| SEC-09 | Caminho absoluto Windows quebra o e-mail fora do Windows | **P1** | `Services/User.cs:329,345` | — |
| BUG-01 | `ToList()[0]` estoura em banco sem admin | **P1** | `CheckPayment.cs:35,118` | — |
| BUG-02 | `int.Parse` de `dueDate` estoura com formato diferente | **P1** | `CheckPayment.cs:47` | — |
| BUG-03 | Token de e-mail com 30 min mas sem single-use | **P1** | `User.cs:25,328,344` | — |
| BUG-04 | `consultCharges` silencia exceção e não marca falha | **P1** | `CheckPayment.cs:122-125` | — |
| TEST-01 | Zero testes (fluxo de pagamento o mais crítico) | **P1** | *(ausente)* | BUG-02, BUG-03 |
| DEVOPS-01 | .NET 5 EOL → .NET 8 LTS + CVEs | **P1** | `condominio_api.csproj:4` | — |
| IMP-01 | Token sem `jti` (sem revogação) | **P2** | `User.cs:174` | SEC-05 |
| IMP-02 | `SendEmail` síncrono trava a requisição | **P2** | `User.cs:293` | — |
| IMP-03 | `URL` de retorno do e-mail hardcoded | **P2** | `User.cs:29` | SEC-08 |
| DEVOPS-02 | `obj/` versionado no git | **P2** | `.gitignore` | — |
| DEVOPS-03 | CI: build + teste | **P2** | novo `.github/workflows/ci.yml` | TEST-01 |
| DEVOPS-04 | Falta `.env.example` | **P2** | *(ausente)* | SEC-05 |
| DOC-01 | README não documenta variáveis e comportamento do timer | **P2** | `README.md` | SEC-02 |
| DOC-02 | Falta `SECURITY.md` | **P3** | *(ausente)* | — |

**Placar: 4 P0 · 11 P1 · 7 P2 · 1 P3 = 23 itens.**

---

## 3. Segurança
### SEC-01 · Senha do Gmail versionada no código · [P0]

- **Arquivo:** `Services/User.cs:297`
- **Evidência:**
  ```csharp
  client.Credentials = new NetworkCredential("seunegocioonlineagr@gmail.com", "35141543Rd");
  ```
  Literal **ativo** no caminho de envio (`SendEmail`, linhas 293-313), e o arquivo está no índice:
  `git ls-files Services/User.cs` → `Services/User.cs`. O método é chamado por `EmailConfimacao`
  (linha 315) e pelo reset de senha (linha ~344), ou seja, toda confirmação de e-mail usa essa
  credencial embutida.
- **Impacto:** conta de Gmail **com senha escrita no GitHub público**
  (`github.com/FrancosCorporation/condominio_api`). Quem lê o repositório pode enviar e-mail como a
  empresa e, dependendo das permissões da conta, ler/modificar mensagens — inclusive interceptar
  os tokens de confirmação de e-mail que o **próprio sistema** envia (`BUG-03`). Se for senha de app
  do Google, revogá-la é urgente; se for a senha da conta, a conta está comprometida.
- **Mudança:**
  1. Mover para `Config/Settings.cs` (o ponto único de segredo), lendo de env:
     ```csharp
     public static string SmtpUser =>
         Environment.GetEnvironmentVariable("SMTP_USER") ?? string.Empty;
     public static string SmtpPassword =>
         Environment.GetEnvironmentVariable("SMTP_PASSWORD") ?? string.Empty;
     ```
     E falhar rápido no `SendEmail` se vierem vazios — nunca com fallback previsível.
  2. **Trocar a senha no Gmail imediatamente** (o valor atual está no histórico público).
  3. Remover o literal do histórico com `git filter-repo` + `git push --force-with-lease`.
  4. Preferível: usar **senha de app dedicada** (2FA ativo) ou serviço de envio transacional
     (SendGrid/SES) em vez da caixa real — o literal de hoje é conta pessoal, não serviço.
- **Aceite:** nenhum literal de credencial no código; e-mail sai usando env; a senha antiga não
  autentica mais.
- **Verificação:**
  ```bash
  grep -rn 'NetworkCredential("seunegocio' Services/ && echo 'FALHA: credencial no codigo' || echo 'OK'
  grep -rn '35141543Rd' . --include='*.cs' && echo 'FALHA' || echo 'OK'
  ```

### SEC-02 · Timer de cobrança a cada 2 segundos · [P0]

- **Arquivo:** `Services/CheckPayment.cs:32`
- **Evidência:**
  ```csharp
  _timer = new Timer(consultCharges, null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
  ```
- **Impacto:** a cada **2 segundos**, 24/7, o serviço lista **todos** os databases do Mongo
  (`consultCharges`, linhas 106-115) e dispara `GET /api-integration/charges` no Juno com o token
  (`Payment.cs:62-68`). São **43.200 consultas/dia ao gateway de pagamento**. Isso é um DDoS
  contra o próprio provedor: bloqueia a conta por rate limit da Juno, consome cota de API e ainda
  força o cache de 1 h do token (`Payment.cs:30-34`) a **nunca** ser problema — quando o problema real
  é a frequência, não a expiração. Além disso, cada ciclo abre conexões Mongo para todos os tenants.
- **Mudança:** (1) intervalo mínimo de **15 min** (`TimeSpan.FromMinutes(15)`), ou melhor, agendar por
  `IHostedService` com `PeriodicTimer` e **jitter** (evitar pico sincronizado); (2) consultar o Juno
  **uma vez** por ciclo e reutilizar a lista de charges para todos os tenants (hoje a consulta é por
  chamada, mas a estrutura do loop sugere repetição por tenant); (3) pular o ciclo quando não houver
  assinatura ativa registrada, em vez de varrer bancos vazios; (4) expor o intervalo por env
  (`PAYMENT_CHECK_MINUTES` com default 15).
- **Aceite:** de 43.200/dia para ≤ 96/dia (15 min) sem perda funcional; log mostra o intervalo efetivo.
- **Verificação:**
  ```bash
  grep -n 'FromSeconds(2)' Services/CheckPayment.cs && echo 'FALHA: ainda 2s' || echo 'OK'
  # apos subir, observar o log por 20 min: esperado 1-2 execucoes, nao 600
  ```

### SEC-03 · `changeIsPayment` atualiza todos os tenants de uma vez · [P0]

- **Arquivo:** `Services/CheckPayment.cs:34-40`
- **Evidência:**
  ```csharp
  UserAdm userAdm = userAdmCollection.Find<UserAdm>(userAdm => true).ToList()[0];
  var update = Builders<UserAdm>.Update.Set("isPayment", isPayment);
  var result = userAdmCollection.UpdateOne(userAdm => true, update);
  ```
  O filtro é `userAdm => true` tanto na leitura (`[0]`, o **primeiro** admin que aparecer) quanto
  na escrita (`UpdateOne` com filtro verdadeiro). O mesmo banco pode ter mais de um `UserAdm` — e
  cada ciclo do `SEC-02` roda isso em **todos** os databases (`consultCharges`, linha 106).
- **Impacto:** quando o pagamento de **um** condomínio é confirmado ou vence, o flag `isPayment`
  pode ser escrito no registro **errado** (sempre o primeiro da coleção, independente de qual
  assinatura mudou) e, por repetir o padrão por banco, o método está a um refactor de distância de
  contaminar outro tenant. Hoje: atualização à conta-gotas sem vínculo com a assinatura que a gerou;
  o laço de `consultCharges` compara `userAdm[0].idSubscription == charge.subscription.id` (linha
  117) mas o `changeIsPayment` **não** recebe o `userAdm` específico — recebe o database e pega o
  `[0]`. Se o banco tiver 2 admins (caso real de teste e de suporte), o errado é marcado como pago.
- **Mudança:** (1) `changeIsPayment` deve receber o `UserAdm` (ou ao menos o `idSubscription`)
  específico que o ciclo identificou — nunca buscar `[0]`; (2) o filtro da escrita deve ser por
  identidade (`userAdm => userAdm.id == alvo.id`), nunca `=> true`; (3) se a intenção é
  "marcar todos pagos quando a conta global confirma", tornar isso explícito com nome e comentário
  diferentes — não no mesmo método de per-tenant.
- **Aceite:** `changeIsPayment(db, X, true)` com 2 admins no banco altera **apenas** o de
  assinatura X; o outro fica intocado.
- **Verificação:**
  ```bash
  # banco de teste com 2 admins, um pago e um nao:
  # chamar changeIsPayment para o pago e conferir que o nao-pago continua isPayment=false
  grep -n 'userAdm => true' Services/CheckPayment.cs    # nao deve existir na escrita
  ```

### SEC-04 · SHA-256 cru (sem sal) no lugar de bcrypt/Argon2 · [P0]

- **Arquivo:** `Services/User.cs:159-172`
- **Evidência:**
  ```csharp
  public string passwordToHash(string password)
  {
      using (SHA256 sHA256 = SHA256.Create())
      {
          byte[] passwordBytes = Encoding.ASCII.GetBytes(password);
  ```
  SHA-256 **sem sal**, e ainda com `ASCII.GetBytes` (trunca silenciosamente qualquer byte > 0x7F —
  senhas com acento ou emoji perdem o final sem aviso). Chamado em `User.cs:42,96,113` (três pontos
  de cadastro) e na comparação de login.
- **Impacto:** igual ao da `api_authentication` irmã (`SEC-03` de lá): GPU quebra senhas em minutos,
  senhas iguais têm hash igual (revela reuso entre usuários), e contas com senha acentuada têm
  hash de **prefixo**, não da senha completa. Como este repo divide o risco de contas com o de lá
  (modelo multi-tenant por database), o vazamento de um banco compromete senhas reutilizadas.
- **Mudança:** (1) trocar por `BCrypt.Net-Next` (ou Argon2), custo ≥ 12;
  (2) gravar marcador de versão no documento (`"alg": "bcrypt12"`) para distinguir do legado;
  (3) manter `passwordToHash` **só** no caminho de verificação de hash antigo, com **re-hash no
  primeiro login bem-sucedido** (migração transparente); (4) corrigir o `ASCII.GetBytes` residual
  para UTF-8 onde SHA-256 ainda for usado na transição.
- **Aceite:** o banco passa a guardar bcrypt ≥ 12; senhas iguais geram hashes diferentes; hashes
  legados migram no login.
- **Verificação:**
  ```bash
  grep -rn 'passwordToHash' Services/    # só no caminho de migração legada
  mongo --eval 'db.usersAdm.findOne({}, {email:1,password:1})'   # hash $2a$/$2b$
  ```
### SEC-05 · Fallback JWT previsível em `DevSecretFallback` · [P1]

- **Arquivo:** `Config/Settings.cs:17`
- **Evidência:**
  ```csharp
  private const string DevSecretFallback = "dev-only-insecure-secret-change-me-please!";
  public static string Secret =>
      Environment.GetEnvironmentVariable("CONDOMINIO_JWT_SECRET")
      ?? DevSecretFallback;
  ```
- **Impacto:** se `CONDOMINIO_JWT_SECRET` não estiver exportada, a API **sobe normalmente** com um
  segredo que está **literalmente legível por qualquer um que leia o repositório** (o nome e o
  valor estão juntos na mesma linha). Qualquer pessoa assina HS256 válido com `role` e
  `nameCondominio` à escolha. É a mesma classe de problema da `api_authentication` (`SEC-01`),
  mas com um nome de variável que **parece** inseguro só para quem lê o código — quem opera nem
  percebe que está sem chave.
- **Mudança:** (1) trocar o fallback por `throw`:
  ```csharp
  .NET
  public static string Secret =>
      Environment.GetEnvironmentVariable("CONDOMINIO_JWT_SECRET")
      ?? throw new InvalidOperationException(
          "CONDOMINIO_JWT_SECRET nao definida. Exporte antes de iniciar (openssl rand -hex 32).");
  ```
  (2) validar comprimento ≥ 32 bytes na inicialização (aproveitar a mesma guarda do `SEC-01`);
  (3) `DevSecretFallback` **some** do código — não existe justificativa para tê-lo em repo público.
- **Aceite:** a API **não inicia** sem a variável; com valor < 32 bytes, recusa com mensagem clara.
- **Verificação:**
  ```bash
  unset CONDOMINIO_JWT_SECRET; dotnet run            # deve falhar com a mensagem
  CONDOMINIO_JWT_SECRET=$(openssl rand -hex 32) dotnet run   # deve subir
  grep -rn 'DevSecretFallback' Config/ && echo 'FALHA' || echo 'OK'
  ```

### SEC-06 · CORS `AllowAnyOrigin` + política nunca registrada · [P1]

- **Arquivo:** `Startup.cs:30-35` (registra a **default** com `AllowAnyOrigin`) e `:74`
  (o pipeline chama `app.UseCors(MyAllowSpecificOrigins)` — política que **não existe**)
- **Evidência:**
  ```csharp
  services.AddCors(options => {
      options.AddDefaultPolicy(builder => { builder.AllowAnyOrigin(); });
  });
  // ...
  app.UseCors(MyAllowSpecificOrigins);   // MyAllowSpecificOrigins nunca foi AddPolicy
  ```
  A variável `MyAllowSpecificOrigins = "_myAllowSpecificOrigins"` é declarada (`Startup.cs:27`)
  mas **nenhum** `options.AddPolicy(...)` a registra. Em .NET, `UseCors(nome-inexistente)` lança
  `InvalidOperationException` no startup — ou seja, **o pipeline atual quebra no boot**, ou foi
  contornado em algum ponto que não está neste código.
- **Impacto:** dois problemas em um: (a) se o `UseCors` cair, a API não sobe; (b) se alguém "corrigir"
  registrando a política como ela está escrita na default, o resultado prático é **`AllowAnyOrigin`
  global** — qualquer origem consome endpoints de pagamento (`Controllers/Payment.cs`) com credencial
  de usuário comum. Num contexto de cobrança, CORS aberto facilita CSRF contra o pagamento.
- **Mudança:** (1) registrar **uma** política nomeada com origens explícitas via env
  (`ALLOWED_ORIGINS`, separadas por `;`):
  ```csharp
  options.AddPolicy(MyAllowSpecificOrigins, builder =>
      builder.WithOrigins(OrigensPermitidas()).AllowAnyHeader().AllowAnyMethod());
  ```
  (2) nunca `AllowAnyOrigin` em API com `AllowCredentials` nem com endpoints de pagamento;
  (3) validar na `Configure` que a lista não está vazia em produção (falha cedo).
- **Aceite:** origem listada funciona; origem de fora **não** recebe `Access-Control-Allow-Origin`;
  a API sobe sem exceção de política.
- **Verificação:**
  ```bash
  curl -sI -X OPTIONS https://<host>/api/payment \
    -H 'Origin: https://evil.example' -H 'Access-Control-Request-Method: POST' \
    -D - | grep -i 'access-control-allow-origin'   # nao deve aparecer para origem de fora
  ```

### SEC-07 · `ValidateToken` estoura sem header (`Split[1]`) · [P1]

- **Arquivo:** `Services/User.cs:195-196` (e padrão repetido do `api_authentication`)
- **Evidência:**
  ```csharp
  string jwtString = request.Headers["Authorization"].ToString().Split(" ")[1];
  ```
  Idêntico defeito ao da `api_authentication` (`SEC-02` de lá): sem header → `Split(" ")` devolve
  array de 1 elemento → `IndexOutOfRangeException` fora do `try/catch` de `SecurityTokenException`.
- **Impacto:** requisição anônima sem header derruba o middleware com `500` em vez de `401`.
  Mesmo DoS trivial, mesmo ruído de log. Copiou-se o bug junto com o arquivo.
- **Mudança:** o mesmo `TryExtractBearer` recomendado na `api_authentication` (ver `SEC-02` de lá):
  validar header antes de indexar; `ValidateToken` devolve `false`, e o chamador trata como 401.
  Idealmente **extrair para um helper compartilhado** entre os dois repos (ou ao menos copiar o
  mesmo padrão, documentando a origem comum).
- **Aceite:** rota protegida sem header devolve `401`, **não** `500`.
- **Verificação:**
  ```bash
  curl -i https://<host>/api/condominio                          # 401
  curl -i -H 'Authorization: Bearer' https://<host>/api/condominio  # 401
  ```

### SEC-08 · SMTP e URL Juno impossíveis de rotacionar sem deploy · [P1]

- **Arquivo:** `Services/User.cs:296` (`new SmtpClient("smtp.gmail.com", 587)`),
  `Services/Payment.cs:28` (`private string _baseUrl = "https://sandbox.boletobancario.com"`),
  `Services/CheckPayment.cs:24` (mesmo literal)
- **Evidência:** hostname do SMTP, URL do sandbox Juno e (até `SEC-01`) credenciais estão **hardcoded**
  em 3 arquivos diferentes.
- **Impacto:** (a) a URL do **sandbox** está colada no caminho de pagamento — se o deploy de produção
  for feito com este binário, **toda cobrança vai para o ambiente de teste** (dinheiro não entra);
  (b) trocar de provedor SMTP ou de ambiente Juno **exige recompilar e republicar**;
  (c) dois literais idênticos (`Payment.cs:28` e `CheckPayment.cs:24`) podem divergir num refactor
  futuro — um serviço falando com produção e outro com sandbox.
- **Mudança:** (1) centralizar em `Config/Settings.cs`: `SmtpHost`, `SmtpPort`, `JunoBaseUrl`, todos por
  env com valores sensatos por ambiente; (2) extrair o literal duplicado para **uma** constante
  compartilhada; (3) exigir `JunoBaseUrl` explícito em produção (falha cedo se for o sandbox).
- **Aceite:** `grep -rn 'sandbox.boletobancario\|smtp.gmail' Services/ Controllers/` → vazio (só no
  `Settings.cs` como default documentado); trocar sandbox→produção é mudança de env, não de código.
- **Verificação:**
  ```bash
  grep -rn 'sandbox.boletobancario' Services/ Controllers/ | grep -v 'Config/Settings.cs' \
    && echo 'FALHA: literal fora do ponto único' || echo 'OK'
  ```
### SEC-09 · Caminho absoluto Windows quebra o e-mail fora do Windows · [P1]

- **Arquivo:** `Services/User.cs:329` e `:345`
- **Evidência:**
  ```csharp
  html = new StreamReader("C:\\Users\\Rodolfo\\git\\condominio_api\\Templates\\confirmed.html").ReadToEnd();
  ```
  Duas ocorrências idênticas (confirmação de e-mail e reset de senha).
- **Impacto:** fora da máquina do desenvolvedor (qualquer Linux, Docker, CI, servidor), a confirmação
  de e-mail e o reset de senha **lançam `FileNotFoundException`** — ou seja, cadastro e recuperação
  de conta **não funcionam em produção**. É defeito funcional disfarçado de item de infra: o caminho
  mais crítico de onboarding está travado na máquina de dev. O `DEVOPS-01` (CI) pegaria isso no
  primeiro run, porque o CI roda em Linux.
- **Mudança:** (1) resolver o template **relativo à raiz da aplicação**:
  `Path.Combine(AppContext.BaseDirectory, "Templates", "confirmed.html")`;
  (2) incluir os `.html` no publish (checar `condominio_api.csproj` para garantir `Content`/`CopyToOutput`);
  (3) extrair para **uma** função `CarregarTemplate(nome)` em vez do `StreamReader` duplicado.
- **Aceite:** rodar em container Linux, confirmar e-mail e resetar senha funcionam sem exceção de arquivo.
- **Verificação:**
  ```bash
  grep -rn 'C:\\\\Users' Services/ Controllers/ && echo 'FALHA: caminho absoluto' || echo 'OK'
  # no container Linux: POST de cadastro + fluxo de confirmacao sem FileNotFoundException
  ```

---

## 4. Bugs e defeitos funcionais

### BUG-01 · `ToList()[0]` estoura em banco sem admin · [P1]

- **Arquivo:** `Services/CheckPayment.cs:35` e `:118`
- **Evidência:**
  ```csharp
  UserAdm userAdm = userAdmCollection.Find<UserAdm>(userAdm => true).ToList()[0];
  List<UserAdm> userAdm = userAdmCollection.Find<UserAdm>(user => true).ToList();
  // ... depois: userAdm[0].idSubscription (linha 118)
  ```
  Sem checar `Count > 0` antes do índice. (A linha 117 faz `if(userAdm.Count > 0 ...)` no loop de
  `consultCharges`, mas o `changeIsPayment` da linha 35 **não** tem a guarda.)
- **Impacto:** primeiro ciclo do timer em banco recém-criado (ou com `usersAdm` vazia) lança
  `ArgumentOutOfRangeException` — e como é `IHostedService`, a exceção **não** cai na rota, cai no
  log do host e potencialmente **mata o loop do timer** (dependendo do tratamento do `Timer`). Um
  condomínio recém-cadastrado, sem concluir o primeiro pagamento, é exatamente o caso que estoura.
- **Mudança:** guardar com `FirstOrDefault()` e tratar nulo (pular o ciclo com log, não com exceção).
  Alinhado ao `SEC-03`: ao pular, **não** marcar nada como pago.
- **Aceite:** banco com `usersAdm` vazia → ciclo pula com log, sem exceção e sem marcar pagamento.
- **Verificação:**
  ```bash
  # banco de teste sem admin: aguardar 1 ciclo e conferir que nao ha excecao no log
  grep -n 'FirstOrDefault' Services/CheckPayment.cs    # deve existir
  ```

### BUG-02 · `int.Parse` de `dueDate` estoura com formato diferente · [P1]

- **Arquivo:** `Services/CheckPayment.cs:47`
- **Evidência:**
  ```csharp
  DateTimeOffset dueDateCharge = new DateTimeOffset(new DateTime(
      int.Parse(charge.dueDate.Split("-")[0]),
      int.Parse(charge.dueDate.Split("-")[1]),
      int.Parse(charge.dueDate.Split("-")[2])));
  ```
  Três `int.Parse` encadeados sobre `Split("-")` **sem** validar o formato. Se `dueDate` vier com
  hora (`2026-10-03T00:00:00`), `Split("-")[2]` vira `"03T00:00:00"` e o `Parse` lança
  `FormatException`.
- **Impacto:** um único charge com formato de data estendido **aborta o loop inteiro** do
  `consultCharges` (a exceção sai do `foreach` e o restante das cobranças do ciclo não é processado).
  Com o timer de `SEC-02` a 2 s, o mesmo charge quebra **todos** os ciclos seguintes — pagamento
  legítimo para de ser registrado por causa de uma data.
- **Mudança:** (1) `DateTimeOffset.TryParse(charge.dueDate, out var due)` com fallback para
  `DateTime.ParseExact` nos formatos conhecidos da Juno; (2) em falha de parse, **logar e pular
  só aquele charge** (não abortar o loop); (3) nunca montar `DateTime` na mão a partir de substring.
- **Aceite:** charge com `dueDate` estendido é pulado com log; os demais do ciclo são processados.
- **Verificação:**
  ```bash
  grep -n 'int.Parse(charge.dueDate' Services/CheckPayment.cs && echo 'FALHA' || echo 'OK'
  # teste com dueDate='2026-10-03T00:00:00' nao deve abortar o ciclo
  ```

### BUG-03 · Token de e-mail com 30 min mas sem single-use · [P1]

- **Arquivo:** `Services/User.cs:25` (`_timeExpiredTokenEMAIL = 0.5`), `:328` (confirmação) e `:344` (reset)
- **Evidência:** `GenerateToken(user, _timeExpiredTokenEMAIL)` gera JWT com `Expires = +0.5 h`
  (`User.cs:174-188`). Não há registro de uso — o mesmo token confirma o e-mail **quantas vezes
  for apresentado** dentro da janela.
- **Impacto:** token de reset de senha reutilizável dentro de 30 min: quem interceptar o link (log
  de proxy, histórico de navegador compartilhado, forwarded email) redefine a senha **repetidas
  vezes**. E como `SEC-01` entrega a caixa de e-mail da empresa, o canal de entrega já é frágil.
  O reset ainda **não invalida** o token após uso bem-sucedido.
- **Mudança:** (1) registrar o `jti` do token de uso único em coleção com TTL (combinar com o
  `IMP-01`); (2) na confirmação e no reset, marcar como **usado** antes de aplicar a ação;
  (3) rejeitar token já usado mesmo dentro da janela de validade.
- **Aceite:** o mesmo link de confirmação/reset funciona **uma vez**; a segunda tentativa é rejeitada.
- **Verificação:**
  ```bash
  # usar o mesmo ?token= duas vezes: primeira 200, segunda 401/410
  ```

### BUG-04 · `consultCharges` silencia exceção e não marca falha · [P1]

- **Arquivo:** `Services/CheckPayment.cs:122-125`
- **Evidência:**
  ```csharp
  catch (System.TimeoutException)
  {
      Console.Write("Empty Database\n");
  }
  ```
  Só captura `TimeoutException`, e o corpo **loga uma mensagem que não descreve o erro**
  (`"Empty Database"` para um timeout). Qualquer outra exceção (rede Juno, `NullReference`
  quando `_junoCharges` é nulo, `FormatException` do `BUG-02`) **escapa** do `Timer`.
- **Impacto:** falha de rede com a Juno num ciclo **mata o callback** do timer (dependendo do
  `Timer`, não agenda o próximo), ou pelo menos perde o ciclo sem registro. Com 2 s de intervalo
  isso parecia "auto-curável"; com o intervalo correto do `SEC-02` (15 min), perder um ciclo sem
  logar é perder 15 min de detecção de pagamento.
- **Mudança:** (1) `catch (Exception ex)` com log real (`ILogger`, incluindo cycle e tenant);
  (2) nunca deixar a exceção escapar do callback do timer — o loop **deve** continuar;
  (3) contar falhas consecutivas e alertar após N (liga ao `DEVOPS-03` de observabilidade... que aqui
  é `DOC-01`, já que não há CI de runtime — na prática, log estruturado é o alerta).
- **Aceite:** simular Juno fora do ar → ciclo registra falha e **continua**; o próximo ciclo tenta de novo.
- **Verificação:**
  ```bash
  # derrubar o endpoint Juno/mock e observar 3 ciclos: todos logam falha, nenhum mata o timer
  grep -n 'catch (System.TimeoutException)' Services/CheckPayment.cs && echo 'FALHA' || echo 'OK'
  ```
---

## 5. Qualidade: testes, arquitetura e observabilidade

### TEST-01 · Zero testes para o fluxo mais crítico (pagamento) · [P1]

- **Arquivo:** *(ausente)* — `find . -path '*test* -o -path '*Test*'` só devolve `obj/` de build.
- **Evidência:** 0 testes num repositório com cobrança por cartão, reconciliação automática e
  multi-tenant. O `CheckPayment` (o componente mais perigoso) nunca foi executado fora de produção.
- **Impacto:** os 5 P1 de bug (`BUG-01`, `BUG-02`, `BUG-04`, mais `SEC-03` e o loop do `SEC-02`)
  **só acontecem em produção**, porque não há ambiente que os reproduza. Refactor de pagamento sem
  rede de segurança é loteria.
- **Mudança:** criar `condominio_api.Tests` (xUnit), priorizando o que é barato e determinístico:
  | Caso | Assertivo |
  |---|---|
  | `int.Parse` de `dueDate` com formato estendido | não aborta o loop (`BUG-02`) |
  | `changeIsPayment` com 2 admins | só o alvo muda (`SEC-03`) |
  | `ToList()[0]` em coleção vazia | pula sem exceção (`BUG-01`) |
  | Token de e-mail usado 2× | segunda tentativa rejeitada (`BUG-03`) |
  | `ValidateToken` sem header | `false`, sem exceção (`SEC-07`) |
  | Fallback JWT ausente | falha na inicialização (`SEC-05`) |
  Usar Mongo de teste em container e mock HTTP para a Juno (nunca bater na sandbox real).
- **Aceite:** `dotnet test` com ≥ 6 casos; reintroduzir o `int.Parse` quebra o build.
- **Verificação:**
  ```bash
  dotnet test    # >= 6 testes, 0 falhas
  ```

### IMP-01 · Token sem `jti` (sem revogação) · [P2]

- **Arquivo:** `Services/User.cs:174-188`
- **Evidência:** o `SecurityTokenDescriptor` define `Subject`, `Expires` e `SigningCredentials` —
  sem `Id`, logo sem claim `jti`. Mesmo defeito do `IMP-01` da `api_authentication`.
- **Impacto:** token de login válido por horas não pode ser invalidado antes de expirar; token de
  e-mail (`BUG-03`) não pode ser invalidado após uso. Sem `jti`, single-use é impossível.
- **Mudança:** `Id = Guid.NewGuid().ToString()` em **todas** as gerações (`GenerateToken` do login
  e as duas de e-mail); denylist de `jti` consumidos com TTL; checar no `ValidateToken`.
- **Aceite:** token gerado tem `jti`; token de e-mail usado é rejeitado na reapresentação.
- **Verificação:**
  ```bash
  # decodificar qualquer token emitido e conferir o campo jti
  ```

### IMP-02 · `SendEmail` síncrono trava a requisição · [P2]

- **Arquivo:** `Services/User.cs:293-313`
- **Evidência:** `client.Send(message)` é **bloqueante** no caminho da requisição HTTP de cadastro e
  de reset — sem `SendAsync`, sem fila, sem timeout configurado.
- **Impacto:** o SMTP do Gmail responde em segundos quando está bem; quando está mal (ou a senha foi
  revogada após o `SEC-01`), **cada** cadastro pendura a thread do request até timeout do TCP.
  Com pouco thread-pool, é DoS indireto: cadastros travam e o servidor para de responder.
- **Mudança:** (1) `SendMailAsync` com `CancellationToken` e timeout explícito; (2) melhor ainda,
  enfileirar o e-mail (mesmo que em coleção Mongo de `email_queue` com worker) para que cadastro
  **nunca** dependa da resposta do SMTP; (3) timeout de conexão configurado (hoje é o default do
  `SmtpClient`, que é generoso demais).
- **Aceite:** cadastro retorna antes do e-mail sair; SMTP fora do ar não trava o request.
- **Verificação:**
  ```bash
  grep -n 'SendMailAsync\|SmtpClient' Services/User.cs   # async, nao bloqueante
  # com SMTP mock fora do ar, POST de cadastro responde em < 5s
  ```

### IMP-03 · `URL` de retorno do e-mail hardcoded para localhost · [P2]

- **Arquivo:** `Services/User.cs:29` (`private string URL = "https://localhost:5001/"`)
- **Evidência:** a base dos links de confirmação/reset (`User.cs:327,343`) é um literal de dev.
- **Impacto:** em produção, o usuário recebe link para **localhost** — clicar não confirma nada.
  É o tipo de item que faz o pagamento "não chegar" mesmo com Juno funcionando.
- **Mudança:** `AppBaseUrl` em `Config/Settings.cs` por env (`APP_BASE_URL`), com validação de
  formato na inicialização; exigir valor explícito em produção (falha cedo se for localhost).
- **Aceite:** o link do e-mail aponta para o host real configurado por env.
- **Verificação:**
  ```bash
  grep -n 'localhost:5001' Services/User.cs && echo 'FALHA' || echo 'OK'
  ```

---

## 6. DevOps / Infra

### DEVOPS-01 · .NET 5 EOL → .NET 8 LTS + CVEs nos pacotes `5.0.3` · [P1]

- **Arquivo:** `condominio_api.csproj:4` e dependências `5.0.3`
- **Evidência:** `<TargetFramework>net5.0</TargetFramework>` (EOL desde 10/05/2022). Pacotes:
  `JwtBearer 5.0.3`, `OpenIdConnect 5.0.3`, `Identity.EntityFrameworkCore 5.0.3`, `Identity.UI 5.0.3`,
  `EntityFrameworkCore.* 5.0.3`, `Microsoft.Identity.Web 1.1.0`. O GitHub já sinalizou CVEs neste repo
  (2 high, 2 moderate — registrado no `.planning` de 21/09).
- **Impacto:** igual ao da `api_authentication` (`DEVOPS-01`/`DEVOPS-03` de lá): runtime sem patch e
  `JwtBearer` antigo no caminho de autenticação. E os pacotes `Identity.*`/`EntityFrameworkCore.*`
  são resíduo de template (o projeto usa **MongoDB**, não EF Core) — aumentam superfície sem uso.
- **Mudança:** (1) `net8.0`; (2) alinhar `Microsoft.*` em `8.0.x`; (3) `MongoDB.Driver 2.12.1` → atual;
  (4) **remover** `Identity.EntityFrameworkCore`, `Identity.UI`, `EntityFrameworkCore.*`,
  `CodeGeneration.Design` (não usados); (5) `dotnet list package --vulnerable --include-transitive`
  vazio; (6) `Newtonsoft.Json` → avaliar `System.Text.Json` nativo (reduz dependência).
- **Aceite:** `dotnet build` em net8.0; `--vulnerable` vazio; pacotes caem de ~11 para ~6.
- **Verificação:**
  ```bash
  sed -i 's|net5.0|net8.0|' condominio_api.csproj
  dotnet restore && dotnet build --warnaserror && dotnet test
  dotnet list package --vulnerable --include-transitive   # vazio
  ```

### DEVOPS-02 · `obj/` versionado no git · [P2]

- **Arquivo:** `.gitignore` (existe) — mas `obj/` já está no índice (mesmo defeito da irmã).
- **Evidência:** o inventário lista `obj/Debug/net5.0/condominio_api.AssemblyInfo.cs` (e outros)
  como arquivos versionados.
- **Impacto:** ruído no histórico e risco de conflito com build local. Detalhe de higiene, mas é o
  tipo de coisa que polui todo diff e esconde mudança real.
- **Mudança:** `git rm -r --cached obj bin` e comitar. O `.gitignore` já cobre — só falta desindexar.
- **Aceite:** `git ls-files | grep -c '^obj/'` → 0.
- **Verificação:**
  ```bash
  git rm -r --cached obj bin 2>/dev/null; git ls-files | grep -c '^obj/'   # 0
  ```

### DEVOPS-03 · CI: build + teste · [P2]

- **Arquivo:** *(ausente)* `.github/workflows/ci.yml`
- **Evidência:** nenhum workflow no repositório.
- **Impacto:** nada impede merge com build quebrado — `SEC-06` (política inexistente) e `SEC-09`
  (caminho Windows) são defeitos que o CI pegaria no primeiro run em Linux.
- **Mudança:** workflow em `on: [push, pull_request]` com `setup-dotnet` (net8.0),
  `dotnet restore`, `dotnet build --warnaserror`, `dotnet test` e
  `--vulnerable --fail-build-on-warn` como barreira (igual ao padrão da `api_authentication`).
- **Aceite:** PR que quebre build ou reintroduza CVE é bloqueado.
- **Verificação:**
  ```bash
  dotnet build --warnaserror && dotnet test
  ```

### DEVOPS-04 · Falta `.env.example` · [P2]

- **Arquivo:** *(ausente)* `.env.example`
- **Evidência:** `.gitignore` cobre `.env`, mas não há exemplo. Após `SEC-05`/`SEC-01`, a API exige
  `CONDOMINIO_JWT_SECRET`, `JUNO_RESOURCE_TOKEN`, `JUNO_AUTHORIZATION`, `JUNO_PLAN_ID`,
  `SMTP_USER`, `SMTP_PASSWORD`, `APP_BASE_URL` — sete variáveis sem documentação.
- **Impacto:** sem exemplo, cada deploy é adivinhação. E o `SEC-02` mostra que falta de variável
  visível leva a fallback inseguro — o exemplo é a prevenção.
- **Mudança:** criar `.env.example` com as 7 + `PAYMENT_CHECK_MINUTES` (do `SEC-02`) e
  `ALLOWED_ORIGINS` (do `SEC-06`), cada uma com `#` explicando origem e formato.
- **Aceite:** `.env.example` versionado; `.env` continua ignorado.
- **Verificação:**
  ```bash
  diff <(grep -rhoE 'GetEnvironmentVariable\("[A-Z_]+' Config/ | sort -u | tr -d '"(' | awk '{print $2}') \
       <(grep -oE '^[A-Z_]+' .env.example | sort -u)    # sem diferencas
  ```

---

## 7. Documentação

### DOC-01 · README não documenta variáveis nem o ciclo de cobrança · [P2]

- **Arquivo:** `README.md` (129 linhas)
- **Evidência:** o README cobre instalação/execução, mas: (a) nenhuma das 7+ variáveis do
  `DEVOPS-04`; (b) nada sobre o timer de reconciliação (frequência, o que ele faz, como observar);
  (c) nada dizendo que sandbox é default (`SEC-08`).
- **Impacto:** quem opera não sabe que a cobrança é reconciliada por background job, nem com que
  frequência, nem que **hoje vai para o sandbox**. É exatamente o tipo de descompasso que faz
  dinheiro não entrar sem alarme.
- **Mudança:** seção "Operação" com: variáveis (linkando `DEVOPS-04`), frequência do ciclo
  (linkando `SEC-02`), como observar (log), sandbox vs produção (linkando `SEC-08`), e aviso de
  que `.env` nunca vai para o git.
- **Aceite:** seguir o README leva a uma API que cobra no ambiente certo, com intervalo visível.
- **Verificação:** `grep -n 'PAYMENT_CHECK_MINUTES\|sandbox\|JUNO' README.md` retorna a seção.

### DOC-02 · Falta `SECURITY.md` · [P3]

- **Arquivo:** *(ausente)* `SECURITY.md`
- **Evidência:** tem `LICENSE`, mas nenhum guia de reporte nem threat model.
- **Impacto:** as decisões duras desta API (claim `nameCondominio` como fronteira de tenant, regra
  "filtro nunca `=> true` em escrita", token de e-mail single-use) existem só neste plano.
  Sem registro, voltam em refactor.
- **Mudança:** criar com: canal de reporte; threat model (forja de token, cobrança para ambiente
  errado, contaminação cross-tenant, interceptação de token de e-mail); e as **três invariantes**
  acima como regra.
- **Aceite:** arquivo existe com as invariantes e o canal.
- **Verificação:** `ls SECURITY.md && grep -c 'invariante\|threat' SECURITY.md`

---

## 8. Ordem de execução (waves)

### Wave 1 — Estancar vazamento e parar o self-DoS (P0)
1. **`SEC-01`** — mover credencial SMTP para env **e trocar a senha no Gmail**. A ordem é:
   primeiro o env, depois a troca, depois o `filter-repo` — senão o e-mail quebra entre um passo e outro.
2. **`SEC-02`** — timer de 2 s para 15 min + consulta única por ciclo + env do intervalo.
3. **`SEC-03`** — `changeIsPayment` com identidade específica (`idSubscription`), nunca `=> true`.
4. **`SEC-04`** — bcrypt com marcador `alg` + re-hash no primeiro login.

> Depois da Wave 1: sem credencial no código, sem DDoS próprio, pagamento marcado no tenant certo.

### Wave 2 — Blindar configuração e e-mail (P1)
5. **`SEC-05`** — `throw` no lugar do fallback JWT.
6. **`SEC-08`** — centralizar SMTP/Juno em `Settings` (exige o novo SMTP do passo 1).
7. **`SEC-09`** — template relativo (desbloqueia CI e e-mail em Linux).
8. **`SEC-07`** — `TryExtractBearer` sem exceção.
9. **`SEC-06`** — política CORS única e explícita (depois do `SEC-07`, para testar com 401 real).
10. **`BUG-01`**, **`BUG-02`** — guardas de coleção vazia e de parse de data.
11. **`BUG-03`** — single-use no token de e-mail (exige o `IMP-01`).
12. **`BUG-04`** — `catch` real com log e continuidade do timer.
13. **`TEST-01`** — travar tudo isso.

### Wave 3 — Plataforma (P1/P2)
14. **`DEVOPS-01`** — net8.0 + remoção dos pacotes de template + `--vulnerable` vazio.
15. **`IMP-01`**, **`IMP-03`**, **`IMP-02`** — `jti`, `APP_BASE_URL`, e-mail async.
16. **`DEVOPS-02`**, **`DEVOPS-03`**, **`DEVOPS-04`** — desindexar `obj/`, CI, `.env.example`.
17. **`DOC-01`** — README de operação.

### Wave 4 — Registro (P3)
18. **`DOC-02`**.

**Dependências que não podem ser invertidas:**
`SEC-01` (env) antes de `SEC-08` (o novo SMTP usa o env) · `IMP-01` (`jti`) antes de `BUG-03`
(single-use precisa de `jti`) · `SEC-09` antes de `DEVOPS-03` (o CI em Linux reprova com caminho
Windows) · `SEC-02` antes de `BUG-04` (o comportamento do timer muda primeiro).

---

## 9. Fora de escopo / riscos

| Item | Decisão | Motivo |
|---|---|---|
| Trocar Juno por outro gateway | **Não** | A integração funciona; os defeitos são de frequência e de isolamento, não de provedor. |
| Migrar Mongo → SQL | **Não** | Fora do escopo. O modelo por database cabe no multi-tenant. |
| Reescrever `Services/Condominio.cs` (904 linhas) | **Não** | Não apresentou defeito comprovado nesta leitura; abrir item próprio sob demanda. |
| Adicionar 2FA/TOTP | **Não, ainda** | Só depois dos P0; expor 2FA antes de fechar conta aberta amplia superfície. |
| Unificar com `api_authentication` | **Não** | Mesma família, propósitos distintos (auth vs. gestão+cobrança). Compartilhar **padrões**, não código. |

**Riscos desta execução:**

- **`SEC-01` exige ação sua em 3 lugares.** A IA move para env; **só você** troca a senha no Gmail
  e reescreve o histórico. Sem a troca, o item está incompleto e a conta continua exposta.
- **`SEC-02` muda o comportamento observável.** De 2 s para 15 min: um teste manual de "paguei e não
  liberou" vai demorar 15 min. Comunique a mudança a quem testa.
- **`SEC-04` invalida senhas se a migração não for feita.** Use `alg` + re-hash no login — nunca
  troca direta de verificador (mesmo aviso da `api_authentication`).
- **`SEC-03` pode expor pagamento pendente antigo.** Ao corrigir o vínculo, cobranças confirmadas
  mas marcadas no registro errado aparecem como "não pagas". Reconciliar o histórico antes de
  aplicar em produção.
- **`DEVOPS-01` (net8.0) pode quebrar `JwtBearer`.** Validar localmente; fallback é net5 **apenas**
  até os CVE saírem.

---

## 10. Definição de pronto (DoD)

**Segurança**
- [ ] `SEC-01` — sem literal SMTP; senha **trocada no Gmail**; histórico reescrito
- [ ] `SEC-02` — ciclo ≥ 15 min, consulta única, intervalo por env e logado
- [ ] `SEC-03` — escrita sempre por identidade (`idSubscription`), nunca por filtro verdadeiro
- [ ] `SEC-04` — senhas em bcrypt ≥ 12; legados migram no login
- [ ] `SEC-05` — sem variável, a API **não inicia**
- [ ] `SEC-06` — uma política CORS explícita; origem de fora bloqueada
- [ ] `SEC-07` — sem header → `401`, nunca `500`
- [ ] `SEC-08` — nenhum literal de host/URL fora de `Settings.cs`
- [ ] `SEC-09` — nenhum `C:\\Users` no código; e-mail funciona em Linux

**Funcional**
- [ ] `BUG-01` — banco vazio pula o ciclo sem exceção
- [ ] `BUG-02` — `dueDate` estendido pula só o charge, sem abortar o ciclo
- [ ] `BUG-03` — link de e-mail funciona **uma vez**
- [ ] `BUG-04` — falha da Juno é logada e o timer continua

**Testes e qualidade**
- [ ] `TEST-01` — ≥ 6 testes, todos passando, no CI
- [ ] `IMP-01` — todo token tem `jti`; usados são rejeitados
- [ ] `IMP-02` — cadastro não pendura sem SMTP
- [ ] `IMP-03` — link aponta para o host real por env

**Infra e documentação**
- [ ] `DEVOPS-01` — `net8.0`, `--vulnerable` vazio, sem pacotes de template
- [ ] `DEVOPS-02` — `obj/` fora do índice
- [ ] `DEVOPS-03` — CI verde e obrigatório
- [ ] `DEVOPS-04` — `.env.example` com as 8 variáveis
- [ ] `DOC-01` — README de operação (variáveis, ciclo, sandbox)
- [ ] `DOC-02` — `SECURITY.md` com as 3 invariantes

**Validação final:**
```bash
dotnet build --warnaserror && dotnet test && dotnet list package --vulnerable
```

---

*Fim do plano. Gerado por leitura direta do código em 2026-10-02. Nenhum item já estava corrigido*
*— todos apontam para defeitos ainda presentes.*
