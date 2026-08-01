# SP-2 — Autenticação de Morador e Sessão Mobile — Design

Data: 2026-08-01

Parte de: [Condotify Mobile — Roadmap](2026-07-31-mobile-roadmap-design.md)
Depende de: [SP-0 — Contratos Compartilhados](2026-07-31-sp0-contratos-compartilhados-design.md)

## Contexto

O diagnóstico inicial apontou que moradores **não conseguem entrar na plataforma**. `AuthController.Login` autentica contra `_context.Users`, que são `UserAccess` — a equipe operacional. `ResidentAccess` é uma entidade de controle de acesso físico, não uma conta.

O aplicativo tem os moradores como público principal. Sem este sub-projeto, o SP-4 não tem para quem entregar metade das telas.

### O que já existe, verificado no banco e no código

| Peça | Situação |
|---|---|
| `ResidentAccessDTO.Email` e `.Password` | Colunas existem. **`Password` é mapeada como `HasMaxLength(150)` e nada mais** — sem hash, sem conversor de criptografia. |
| Dados reais | 4 moradores cadastrados, **nenhum com senha preenchida**. A coluna nunca foi usada. |
| `ResidentUnitLinkDTO` | Já existe e resolve o vínculo morador↔unidade: `Relationship`, `IsPrimary`, `IsActive`, `StartsAt`, `EndsAt`. 4 vínculos no banco. |
| `ResidentAccessTypeEnum` | `Default`, `Responsible`, `NonResponsible`, `Guest`, `ServiceProvider`. |
| Convite de cadastro | `POST /api/public/registration-invites/{token}/complete` já coleta nome, e-mail, telefone, CPF, RG e nascimento, e marca `FirstAccess = false`. **Nunca define senha.** |
| `IJwtTokenService` | `CreateAccessToken(UserAccess user)` — tipado a `UserAccess`, 8 horas, claims `sub`, `nameidentifier`, `email`, `enterprise_id`, `access_type`. |
| `IPasswordHasher<UserAccess>` | Registrado e em uso, padrão ASP.NET Core Identity. |

Duas conclusões práticas. Primeira: **a coluna `Password` de morador guardaria senha em texto claro** se alguém a preenchesse hoje — é uma falha latente que este sub-projeto elimina antes de existir dado afetado. Segunda: como não há senha alguma gravada, dá para introduzir hash correto desde o início, **sem migração de dados**.

### O que falta para uma sessão mobile decente

O mesmo diagnóstico registrou que a API não tem:

- refresh token — o JWT vive 8 horas e depois o usuário simplesmente cai;
- endpoint de logout — não há como invalidar nada;
- recuperação de senha — não existe.

Isso já dói na web. No aplicativo, que fica meses instalado, é inaceitável.

## Escopo

Dentro do escopo:
- Hash de senha para moradores, com o mesmo mecanismo já usado para a equipe.
- Definição de senha no fluxo de convite, que é onde o morador entra na plataforma.
- Login de morador, endpoint separado do login da equipe.
- JWT que distingue morador de equipe, e autorização por unidade.
- Refresh token, logout e revogação.
- Recuperação de senha por e-mail, reaproveitando o SMTP já configurado.
- Troca de senha pelo próprio morador.

Fora do escopo:
- Telas. O SP-2 é backend; o consumo é do SP-4.
- Cadastro público de morador sem convite. Continua sendo a administração que cria o morador e emite o convite.
- Federação, login social, biometria no servidor. A biometria do SP-4 é local ao aparelho e apenas destrava o refresh token guardado.
- Alterar o login da equipe. `AuthController.Login` fica como está.

### Por que endpoints separados em vez de um login unificado

Um único `POST /api/auth/login` que tentasse `UserAccess` e depois `ResidentAccess` teria dois problemas sérios. Criaria um oráculo de enumeração — a diferença de tempo entre os dois caminhos revela em qual tabela o e-mail existe. E um mesmo e-mail poderia existir nas duas tabelas, tornando ambíguo quem está entrando.

Endpoints separados (`/api/auth/login` e `/api/auth/resident/login`) mantêm cada caminho simples e auditável. O aplicativo sabe qual usar porque o usuário escolhe o perfil na primeira entrada.

## Modelo de autorização

Este é o ponto onde o SP-2 diverge mais do que existe.

A equipe usa `LicensePermissionEnum`, um conjunto de flags por licença inteira. Um morador **não** cabe nesse modelo: ele não tem permissão sobre o condomínio, tem direitos sobre **as unidades a que está vinculado**.

Introduz-se `IResidentAuthorizationService`, análogo a `ILicenseAuthorizationService`:

```csharp
public sealed record ResidentAccessGrant(
    Guid ResidentId,
    Guid LicenseId,
    IReadOnlyCollection<Guid> UnitIds,
    ResidentAccessTypeEnum AccessType,
    bool IsResponsible);

public interface IResidentAuthorizationService
{
    Task<ResidentAccessGrant?> GetGrantAsync(ClaimsPrincipal principal, CancellationToken ct = default);
    Task<bool> CanAccessUnitAsync(ClaimsPrincipal principal, Guid unitId, CancellationToken ct = default);
}
```

As unidades vêm de `ResidentUnitLinks` filtradas por `IsActive` e vigência (`StartsAt <= agora < EndsAt`), **resolvidas a cada requisição**, nunca lidas do token. Um vínculo revogado precisa deixar de valer imediatamente, e não quando o JWT expirar.

`Responsible` é quem pode agir em nome da unidade — convidar visitantes, reservar áreas comuns. `NonResponsible`, `Guest` e `ServiceProvider` só consultam. O detalhe de qual perfil pode o quê fica com cada módulo, não com este sub-projeto.

### O que vai no token, e o que não vai

No JWT: `sub`, `principal_type` (`user` ou `resident`), `enterprise_id`, `license_id` e `email`.

**Fora do JWT:** a lista de unidades. Ela muda, pode ser longa, e colocá-la no token significa que revogar um vínculo não tem efeito até a expiração. Resolver por requisição custa uma consulta indexada e vale a correção.

O claim `principal_type` é o que impede a confusão mais perigosa deste desenho: um token de morador ser aceito por um endpoint da equipe. Toda rota existente que hoje usa `[Authorize]` passa a exigir `principal_type = user`, por política padrão. Rotas de morador exigem `resident`.

## Refresh token

Tabela nova, `ResidentRefreshTokens` e `UserRefreshTokens` — ou uma única `RefreshTokens` com discriminador. Colunas: `Id`, `SubjectId`, `SubjectType`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `ReplacedByHash`, `DeviceLabel`, `CreatedIp`.

Regras:

- O refresh token é opaco, aleatório de 256 bits, **guardado apenas como hash SHA-256**, como já se faz com `RegistrationInvite.TokenHash`.
- Validade de 60 dias, renovada a cada uso (rotação).
- **Rotação com detecção de reuso:** ao usar um refresh token, ele é revogado e substituído. Se um token já revogado for apresentado, toda a cadeia daquele dispositivo é revogada — é o sinal clássico de token roubado.
- O access token cai para **1 hora**. Hoje são 8; com refresh, uma janela menor é gratuita em conveniência e melhor em segurança.
- Logout revoga o refresh token apresentado. "Encerrar todas as sessões" revoga todos do usuário.

O access token continua sem estado — não há revogação instantânea dele, e isso é aceito conscientemente: a janela máxima de abuso passa a ser 1 hora em vez de 8.

## Recuperação de senha

`POST /api/auth/resident/password/forgot` recebe um e-mail e **sempre responde 202**, independentemente de o e-mail existir. Responder 404 para e-mail inexistente entrega um enumerador de contas.

O e-mail leva um token de uso único, hash em banco, validade de 30 minutos. `POST /api/auth/resident/password/reset` consome o token e define a senha nova.

O envio reaproveita o `AlertNotificationChannelSender`, que já resolve SMTP a partir do banco ou do ambiente. Se não houver SMTP configurado, o endpoint continua respondendo 202 e registra em log que o envio não ocorreu — o comportamento visível não pode denunciar a ausência de configuração.

## Política de senha

A mesma já aplicada à equipe em `AuthController.ValidateNewPassword`: 8 a 100 caracteres, com maiúscula, minúscula, dígito e caractere especial. Reaproveitar a regra existente em vez de criar uma segunda, divergente.

## Endpoints novos

| Método | Rota | Autenticação | Função |
|---|---|---|---|
| `POST` | `/api/auth/resident/login` | anônima | Login de morador |
| `POST` | `/api/auth/refresh` | anônima | Troca refresh por novo par de tokens |
| `POST` | `/api/auth/logout` | autenticada | Revoga o refresh apresentado |
| `POST` | `/api/auth/logout/all` | autenticada | Revoga todos os refresh do sujeito |
| `GET` | `/api/auth/sessions` | autenticada | Lista dispositivos com sessão ativa |
| `POST` | `/api/auth/resident/password/forgot` | anônima | Inicia recuperação |
| `POST` | `/api/auth/resident/password/reset` | anônima | Conclui recuperação |
| `POST` | `/api/auth/resident/password/change` | morador | Troca a própria senha |
| `GET` | `/api/resident/me` | morador | Perfil, licença e unidades vinculadas |

Alteração em endpoint existente: `POST /api/public/registration-invites/{token}/complete` passa a aceitar `Password` e a defini-la com hash. Sem senha, o morador não consegue entrar — hoje o convite completa e não cria acesso nenhum.

## Riscos

| Risco | Grau | Mitigação |
|---|---|---|
| Token de morador aceito em rota da equipe | **Alto** | Claim `principal_type` exigido por política; teste que tenta cada rota da equipe com token de morador |
| Senha de morador gravada em texto claro | **Alto** | `IPasswordHasher<ResidentAccess>`; teste que falha se a coluna contiver a senha original |
| Vínculo revogado continuar valendo | **Alto** | Unidades resolvidas por requisição, nunca do token |
| Enumeração de contas | Médio | 202 sempre em `forgot`; rate limiting no login; mensagem única para credencial inválida |
| Refresh token roubado | Médio | Rotação com detecção de reuso, revogando a cadeia |
| Morador ver dado de outra unidade | **Alto** | `CanAccessUnitAsync` no servidor, em cada rota de morador |

## Compatibilidade

Nada do login da equipe muda de comportamento, exceto a duração do access token, que cai de 8 horas para 1 — compensada pelo refresh. Isso **é** uma mudança visível para a web: sessões passam a depender do refresh. O `LoginController` da web precisará usá-lo, e isso é trabalho do SP-2, não do SP-4.

A migração acrescenta tabelas novas e não altera colunas existentes, exceto por passar `ResidentAccess.Password` a guardar hash — inócuo, pois nenhuma linha tem valor.
