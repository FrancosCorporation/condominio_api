# MELHORIAS — condominio_api

> Auditoria retomada em 2026-10-11 sobre a branch master, commit a174244.
> Este arquivo acompanha a implementação, o que falta para produção e as verificações realizadas.
> O serviço processa cobrança recorrente e dados pessoais; valide configuração e isolamento antes
> de publicar uma nova instância.

## Estado desta execução

### Implementado no código

| ID | Resultado |
|---|---|
| SEC-01 | Credenciais SMTP são lidas do ambiente. Nenhuma credencial SMTP está no código atual. A rotação da senha exposta no histórico e a limpeza desse histórico continuam pendentes do responsável pela conta e dos clones. |
| SEC-02 | Conciliação a cada 15 minutos por padrão, intervalo configurável entre 15 e 1440 minutos; não consulta a Juno sem assinatura ativa. |
| SEC-03 | Atualização de isPayment é filtrada pelo ID do administrador no database do condomínio. |
| SEC-04 | Novas senhas usam BCrypt12; hashes SHA-256 antigos são migrados no primeiro login válido. |
| SEC-05 | A API falha ao iniciar sem segredo JWT de pelo menos 32 bytes UTF-8. |
| SEC-06 | CORS usa uma política nomeada e origens explícitas; produção exige ALLOWED_ORIGINS. |
| SEC-07 | Extração do Bearer é segura, a assinatura e a validade do JWT são verificadas e rotas autenticadas respondem antes de acessar os serviços. |
| PAY-01 | A consulta manual de cobranças exige administrador e filtra a resposta pela assinatura do condomínio autenticado. |
| SEC-08 | URL da Juno, SMTP e URL dos links de e-mail vêm de configuração. Produção rejeita sandbox. |
| SEC-09 | Template de e-mail usa caminho relativo e é copiado para build/publicação. |
| BUG-01 | A conciliação ignora databases sem administradores ou assinatura. |
| BUG-02 | Datas inválidas são registradas e ignoradas sem interromper o restante do ciclo. |
| BUG-03 | Tokens de confirmação e redefinição têm finalidade própria, jti, expiração de 30 minutos e uso único registrado no MongoDB. |
| BUG-04 | Erros da Juno e por condomínio são registrados; o finally libera o timer para o próximo ciclo. |
| DEVOPS-01 | Runtime atualizado para .NET 10; dependências de template foram removidas e dependências vulneráveis corrigidas/atualizadas. |
| IMP-01 | Todos os JWTs incluem jti; tokens de e-mail também são consumidos uma única vez. |
| IMP-02 | Envio SMTP e leitura do template são assíncronos; falhas retornam erro operacional e ficam no log. |
| IMP-03 | Links de e-mail usam APP_BASE_URL; o link de redefinição abre um formulário e envia a senha no corpo do POST. |
| DEVOPS-02 | bin/, obj/ e publish/ são ignorados pelo Git. |
| DEVOPS-03 | Workflow de build e auditoria NuGet adicionado; resultado remoto ainda pendente do push desta execução. |
| DEVOPS-04 | .env.example documenta variáveis sem incluir credenciais reais. |
| DOC-01 | README refeito com configuração, operação, ambientes Juno e conciliação. |
| DOC-02 | SECURITY.md registra canal de reporte e invariantes de segurança. |

### Ainda pendente

- Rotacionar a credencial SMTP antiga. O segredo foi exposto em commits históricos. Configure a
  nova credencial de aplicativo/serviço no ambiente e revogue a antiga.
- Decidir e coordenar a limpeza do histórico Git que contém a credencial antiga. A reescrita altera
  os hashes dos commits e exige sincronizar todos os clones; não basta remover o texto do arquivo
  atual.
- Criar testes automatizados para autenticação, tokens de uso único e conciliação de pagamento.
  Não há projeto de testes neste repositório; esta execução validou build e auditoria sem rodar testes.
- Configurar e validar o ambiente de produção com MongoDB, SMTP, Juno, CORS e host público reais.
  As credenciais não estão neste repositório.

## Configuração de produção

Defina as variáveis abaixo no serviço que hospeda a API. .env.example contém valores ilustrativos
para desenvolvimento; o processo .NET não carrega .env automaticamente.

| Variável | Uso |
|---|---|
| CONDOMINIO_JWT_SECRET | Assinatura JWT; mínimo 32 bytes UTF-8. |
| CondominioDatabaseSetting__ConnectionString | Conexão MongoDB, incluindo acesso de escrita a userscondominio.usedEmailTokens. |
| CondominioDatabaseSetting__DatabaseName | Database de configuração principal. |
| JUNO_BASE_URL | URL HTTPS de produção da Juno; obrigatória e explícita em produção. |
| JUNO_RESOURCE_TOKEN | Token de recurso da Juno. |
| JUNO_AUTHORIZATION | Cabeçalho de autorização OAuth da Juno. |
| JUNO_PLAN_ID | Plano de assinatura Juno. |
| PAYMENT_CHECK_MINUTES | Intervalo inteiro de 15 a 1440 minutos; padrão 15. |
| SMTP_HOST, SMTP_PORT | Host e porta SMTP. |
| SMTP_USER, SMTP_PASSWORD | Credenciais SMTP novas e rotacionadas. |
| APP_BASE_URL | URL HTTPS pública usada nos links transacionais. |
| ALLOWED_ORIGINS | Origens HTTP/HTTPS permitidas, separadas por ponto e vírgula, sem caminhos e sem asterisco. |

O timer inicia um ciclo na inicialização e, depois, executa no intervalo configurado. Ele lista
assinaturas locais, consulta as cobranças Juno uma vez e atualiza apenas o administrador associado
à assinatura encontrada. Se não houver assinatura local, não chama a Juno.

## Verificações

Comandos de qualidade usados nesta execução:

~~~bash
dotnet build condominio_api.csproj --configuration Release --warnaserror
dotnet list condominio_api.csproj package --vulnerable --include-transitive
git diff --check
~~~

O workflow .github/workflows/ci.yml executa build Release com warnings como erros e auditoria
transitiva de pacotes em cada push e pull request. A automação não substitui uma validação
integrada com MongoDB, SMTP e a conta Juno real.

## Escopo preservado

- A integração e o caminho de cobrança Juno continuam no mesmo fluxo funcional.
- A rota de consulta manual não expõe mais cobranças de outros condomínios.
- Cada condomínio mantém database próprio no MongoDB.
- Nenhum segredo ou dado de morador deve ser incluído nos logs, exemplos ou commits.
- Não reescreva o histórico público sem coordenar a rotação do segredo e todos os clones.
