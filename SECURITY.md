# Segurança

Reporte vulnerabilidades pelo recurso de Security Advisories do GitHub:
https://github.com/FrancosCorporation/condominio_api/security/advisories/new

Inclua o endpoint ou componente afetado, os passos para reproduzir e o impacto observado. Não publique
tokens, credenciais ou dados reais de moradores em issues públicas.

## Invariantes de segurança

1. Só use claims de JWT depois de validar assinatura, algoritmo e validade; tokens de confirmação e
   redefinição devem também validar a finalidade e ser consumidos uma única vez.
2. Toda consulta ou alteração de dados de usuário deve usar o condomínio e o identificador obtidos
   do JWT validado. Uma escrita de pagamento nunca pode atualizar todos os usuários do database.
3. Credenciais e destinos de integração vêm de configuração externa. Em produção, a API deve exigir
   chave JWT, origens CORS, SMTP e credenciais e URL de produção da Juno.

## Áreas sensíveis

- Isolamento entre databases de condomínios.
- Criação e consulta de cobranças e atualização do estado de pagamento.
- Tokens JWT e links enviados por e-mail.
- Variáveis de ambiente e conexão com MongoDB.
