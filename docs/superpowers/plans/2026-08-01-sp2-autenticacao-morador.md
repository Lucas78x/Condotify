# SP-2 — Autenticação de Morador e Sessão Mobile — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Permitir que moradores entrem na plataforma com conta própria, com direitos resolvidos pelas unidades a que estão vinculados, e dar a toda a plataforma uma sessão adequada a um aplicativo que fica meses instalado.

**Architecture:** Moradores recebem endpoints de autenticação separados dos da equipe, evitando o oráculo de enumeração de um login unificado. O JWT ganha o claim `principal_type`, e a política padrão de autorização passa a exigir `user` — de modo que toda rota existente vira automaticamente exclusiva da equipe, sem editar nenhuma delas. Direitos de morador vêm de `ResidentUnitLinks`, resolvidos a cada requisição em vez de carregados no token, para que revogar um vínculo tenha efeito imediato. Refresh tokens opacos com rotação e detecção de reuso substituem o access token de 8 horas por um de 1 hora.

**Tech Stack:** .NET 8, EF Core (PostgreSQL), ASP.NET Core Identity `PasswordHasher`, JWT Bearer, xUnit 2.5.3.

Spec: [SP-2 Design](../specs/2026-08-01-sp2-autenticacao-morador-design.md) · Roadmap: [Condotify Mobile](../specs/2026-07-31-mobile-roadmap-design.md)

## Global Constraints

- Tudo em **net8.0**.
- **Senha de morador nunca é gravada, registrada ou devolvida em texto claro.** A coluna `ResidentAccess.Password` hoje é uma string sem hash e sem conversor; nenhuma linha tem valor, então a correção não exige migração de dados — mas exige que nada volte a gravar texto claro ali.
- **Um token de morador nunca pode ser aceito numa rota da equipe.** Esta é a segunda restrição central. A política padrão faz o trabalho; nenhuma rota existente deve precisar de edição.
- **Unidades acessíveis são resolvidas a cada requisição, nunca lidas do token.**
- Toda resposta a credencial inválida é idêntica, independentemente de o e-mail existir. `password/forgot` responde `202` sempre.
- Convenções da API: `[ApiController]`, `[Authorize]`, DTOs manuais em `CondotifyAPI/Data/`, `DatabaseContext` injetado direto.
- **Nenhuma alteração em `Condotify.UI`.** As Tasks 5 e 9 alteram `Condotify`, `Condotify.Contracts` e `Condotify.ApiClient` deliberadamente; as demais não.

> **Correção do plano aplicada em 2026-08-01, durante a execução.** A restrição original dizia que só a Task 9 tocaria o projeto web. A Task 5 foi bloqueada por isso, com razão: `Condotify/Components/Pages/RegistrationInvite.razor` é o **único** chamador de `POST /api/public/registration-invites/{token}/complete`, e `CompleteRegistrationInviteViewModel` (`Condotify.Contracts/LicenseManagementViewModels.cs:933`) não tem campo de senha. Exigir senha só no servidor quebraria a única via de cadastro que existe, trocando "o morador não consegue entrar" por "o morador não consegue se cadastrar". Tratar o convite como backend-only foi erro de planejamento: o fluxo é inerentemente ponta a ponta.
- Migrações via `dotnet ef migrations add <Nome> --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`. **Ler toda migração gerada antes de aplicar**: o banco de desenvolvimento tem dados reais.
- **Nunca usar `git add -A` nem `git add .`.** Devem permanecer não commitados: `Condotify/Properties/launchSettings.json`, `CondotifyAPI/Properties/launchSettings.json` e `contexto.txt`.
- Não perturbar o contêiner `condotify-postgres`: sem `docker compose down`, sem remover volumes.
- Comandos a partir de `D:\repos\Condotify`.

### Mudança de comportamento aceita e sinalizada

O access token cai de **8 horas para 1 hora**. Todas as sessões existentes, web inclusive, deixam de valer no momento do deploy, por dois motivos: a duração muda e os tokens antigos não carregam `principal_type`, que a política padrão passa a exigir. Isso é deliberado e precisa constar nas notas de release. A Task 9 adapta o `LoginController` da web ao refresh, e sem ela a web passa a deslogar de hora em hora.

### Estado inicial verificado

4 moradores em `Resident`, **nenhum com senha**. 4 vínculos em `ResidentUnitLinks`. 2 licenças. `IPasswordHasher<UserAccess>` já registrado. `AuthController.ValidateNewPassword` já define a política de senha da equipe.

---

## File Structure

**Criados:**

| Arquivo | Responsabilidade |
|---|---|
| `CondotifyAPI/Services/Authorization/ResidentAuthorizationService.cs` | Resolve licença e unidades de um morador |
| `CondotifyAPI/Services/Security/RefreshTokenService.cs` | Emite, valida, rotaciona e revoga refresh tokens |
| `CondotifyAPI/Services/Security/PasswordPolicy.cs` | Regra única de senha, extraída de `AuthController` |
| `CondotifyAPI/Controllers/ResidentAuthController.cs` | Login, senha e perfil do morador |
| `CondotifyAPI/Controllers/SessionController.cs` | Refresh, logout, sessões — serve equipe e morador |
| `CondotifyAPI/Data/Login/ResidentAuthDtos.cs` | DTOs de entrada e saída |
| `CondotifyAPI.Domain/DTO/Users/RefreshTokenDTO.cs` | Entidade de refresh token |
| `CondotifyAPI.Infrastructure/ContextConfiguration/User/RefreshTokenConfiguration.cs` | Mapeamento |
| Migrações EF | Hash de senha e tabela de refresh |
| Testes | Um arquivo por serviço novo |

**Modificados:**

| Arquivo | Alteração |
|---|---|
| `CondotifyAPI/Jwt/JwtTokenService.cs` | `principal_type`; sobrecarga para morador; 1 hora |
| `CondotifyAPI/Program.cs` | Política padrão exigindo `user`; política `Resident`; registros |
| `CondotifyAPI/Controllers/AuthController.cs` | Devolve refresh; usa `PasswordPolicy` |
| `CondotifyAPI/Controllers/PublicRegistrationController.cs` | Define senha no convite |
| `CondotifyAPI.Infrastructure/.../ResidentAccessConfiguration.cs` | Comentário de que a coluna guarda hash |
| `Condotify/Controllers/LoginController.cs` | Usa refresh (Task 9) |

---

## Task 1: Política de senha única e hash de morador

**Files:**
- Create: `CondotifyAPI/Services/Security/PasswordPolicy.cs`
- Modify: `CondotifyAPI/Controllers/AuthController.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/PasswordPolicyTests.cs`

**Interfaces:**
- Produces: `CondotifyAPI.Services.Security.PasswordPolicy.Validate(string? password)` → `string?` (mensagem de erro ou `null`); registro de `IPasswordHasher<ResidentAccess>`.

- [ ] **Step 1: Escrever os testes que falham**

`CondotifyAPI.Tests/PasswordPolicyTests.cs`:

```csharp
using CondotifyAPI.Services.Security;
using Xunit;

namespace CondotifyAPI.Tests;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("Abcdef1!")]
    [InlineData("Senha@2026")]
    public void Validate_AcceptsAConformingPassword(string password) =>
        Assert.Null(PasswordPolicy.Validate(password));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Abc1!")]
    public void Validate_RejectsTooShortOrEmpty(string? password) =>
        Assert.NotNull(PasswordPolicy.Validate(password));

    [Theory]
    [InlineData("abcdefg1!")]   // sem maiuscula
    [InlineData("ABCDEFG1!")]   // sem minuscula
    [InlineData("Abcdefgh!")]   // sem digito
    [InlineData("Abcdefg12")]   // sem caractere especial
    public void Validate_RejectsMissingCharacterClasses(string password) =>
        Assert.NotNull(PasswordPolicy.Validate(password));

    [Fact]
    public void Validate_RejectsOverlyLongPassword() =>
        Assert.NotNull(PasswordPolicy.Validate(new string('A', 60) + new string('b', 45) + "1!"));

    [Fact]
    public void Validate_DoesNotEchoThePasswordInItsMessage()
    {
        var message = PasswordPolicy.Validate("segredo");
        Assert.NotNull(message);
        Assert.DoesNotContain("segredo", message);
    }
}
```

O último teste importa: uma mensagem de erro que ecoa a senha acaba em log.

- [ ] **Step 2: RED** — `dotnet test CondotifyAPI.Tests --filter PasswordPolicyTests`, falha de compilação.

- [ ] **Step 3: Implementar**

`CondotifyAPI/Services/Security/PasswordPolicy.cs`:

```csharp
namespace CondotifyAPI.Services.Security;

/// <summary>
/// Regra unica de senha da plataforma. Extraida de AuthController para que
/// equipe e morador nao acabem com politicas divergentes.
/// </summary>
public static class PasswordPolicy
{
    public const int MinimumLength = 8;
    public const int MaximumLength = 100;

    /// <summary>Devolve a mensagem de erro, ou null quando a senha e valida.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength || password.Length > MaximumLength)
            return $"A senha deve ter entre {MinimumLength} e {MaximumLength} caracteres.";

        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) ||
            !password.Any(char.IsDigit) || !password.Any(x => !char.IsLetterOrDigit(x)))
            return "Use letras maiusculas e minusculas, numero e caractere especial.";

        return null;
    }
}
```

- [ ] **Step 4: GREEN** — os testes passam.

- [ ] **Step 5: `AuthController` passa a usar a política**

Substituir o corpo de `ValidateNewPassword` por `PasswordPolicy.Validate(password)` — ou remover o método privado e chamar a política diretamente nos dois pontos de uso. **Não alterar as mensagens**, que já são as mesmas.

- [ ] **Step 6: Registrar o hasher de morador**

Em `Program.cs`, junto ao hasher existente:

```csharp
builder.Services.AddScoped<IPasswordHasher<ResidentAccess>, PasswordHasher<ResidentAccess>>();
```

Requer `using CondotifyAPI.Domain.Models.Resident;`.

- [ ] **Step 7: Build, suíte, commit**

```bash
git add CondotifyAPI/Services/Security/PasswordPolicy.cs CondotifyAPI/Controllers/AuthController.cs CondotifyAPI/Program.cs CondotifyAPI.Tests/PasswordPolicyTests.cs
git status --short
git commit -m "refactor: extract the password policy so staff and residents share one rule"
```

---

## Task 2: `principal_type` no token e política padrão exigindo equipe

Esta é a task que impede a confusão mais perigosa do sub-projeto.

**Files:**
- Modify: `CondotifyAPI/Jwt/JwtTokenService.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/JwtPrincipalTypeTests.cs`

**Interfaces:**
- Produces:
  - `CondotifyAPI.Jwt.PrincipalTypes` — `const string Claim = "principal_type"`, `const string User = "user"`, `const string Resident = "resident"`.
  - `IJwtTokenService.CreateResidentAccessToken(ResidentAccessDTO resident, Guid licenseId)`.
  - Políticas `"Staff"` (padrão) e `"Resident"`.

- [ ] **Step 1: Escrever os testes que falham**

`CondotifyAPI.Tests/JwtPrincipalTypeTests.cs` — decodificar o JWT emitido e conferir os claims:

```csharp
using System.IdentityModel.Tokens.Jwt;
using CondotifyAPI.Jwt;
using Xunit;

namespace CondotifyAPI.Tests;

public class JwtPrincipalTypeTests
{
    [Fact]
    public void StaffToken_CarriesPrincipalTypeUser()
    {
        var token = ReadToken(CreateService().CreateAccessToken(SampleUser()));

        Assert.Equal(PrincipalTypes.User, token.Claims.First(x => x.Type == PrincipalTypes.Claim).Value);
    }

    [Fact]
    public void ResidentToken_CarriesPrincipalTypeResident()
    {
        var token = ReadToken(CreateService().CreateResidentAccessToken(SampleResident(), LicenseId));

        Assert.Equal(PrincipalTypes.Resident, token.Claims.First(x => x.Type == PrincipalTypes.Claim).Value);
    }

    [Fact]
    public void ResidentToken_CarriesTheLicenseButNoUnitList()
    {
        var token = ReadToken(CreateService().CreateResidentAccessToken(SampleResident(), LicenseId));

        Assert.Equal(LicenseId.ToString(), token.Claims.First(x => x.Type == "license_id").Value);
        Assert.DoesNotContain(token.Claims, x => x.Type is "unit_id" or "unit_ids" or "units");
    }

    [Fact]
    public void AccessTokens_LastOneHour()
    {
        var token = ReadToken(CreateService().CreateAccessToken(SampleUser()));
        var lifetime = token.ValidTo - token.ValidFrom;

        Assert.InRange(lifetime.TotalMinutes, 59, 61);
    }

    private static JwtSecurityToken ReadToken(string jwt) => new JwtSecurityTokenHandler().ReadJwtToken(jwt);
    // CreateService, SampleUser, SampleResident e LicenseId: preencher seguindo
    // o construtor real de JwtTokenService, que le a configuracao.
}
```

O terceiro teste é o mais importante: ele trava a decisão de **não** colocar unidades no token. Se alguém as acrescentar "para economizar uma consulta", este teste falha e força a discussão.

- [ ] **Step 2: RED.**

- [ ] **Step 3: Acrescentar `PrincipalTypes` e a sobrecarga**

Criar `CondotifyAPI/Jwt/PrincipalTypes.cs`, e em `JwtTokenService` acrescentar o claim `principal_type` ao token da equipe e criar `CreateResidentAccessToken`. O token de morador leva `sub`, `nameidentifier`, `email`, `enterprise_id`, `license_id` e `principal_type = resident`. **Não** leva unidades.

Alterar a expiração de `AddHours(8)` para `AddHours(1)` nos dois casos.

- [ ] **Step 4: GREEN.**

- [ ] **Step 5: Política padrão exigindo equipe**

Em `Program.cs`, após `AddAuthentication`:

```csharp
builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .RequireClaim(PrincipalTypes.Claim, PrincipalTypes.User)
        .Build())
    .AddPolicy("Resident", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(PrincipalTypes.Claim, PrincipalTypes.Resident));
```

Isto é o ponto central da task: **toda rota que hoje usa `[Authorize]` passa a exigir `principal_type = user` sem que nenhuma delas seja editada.** A direção do erro é segura — esquecer de anotar uma rota a torna inacessível ao morador, não acessível.

- [ ] **Step 6: Verificar que nenhuma rota da equipe foi esquecida**

Run: `grep -rn "\[Authorize" CondotifyAPI/Controllers/ | grep -v "AllowAnonymous"`
Conferir que nenhuma usa uma política que contorne o padrão. `[Authorize]` sem argumento herda o padrão, que é o desejado.

- [ ] **Step 7: Build, suíte, commit**

```bash
git add CondotifyAPI/Jwt CondotifyAPI/Program.cs CondotifyAPI.Tests/JwtPrincipalTypeTests.cs
git status --short
git commit -m "feat: distinguish staff and resident principals in the access token"
```

---

## Task 3: Autorização por unidade

**Files:**
- Create: `CondotifyAPI/Services/Authorization/ResidentAuthorizationService.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/ResidentAuthorizationServiceTests.cs`

**Interfaces:**
- Produces: `ResidentAccessGrant(Guid ResidentId, Guid LicenseId, IReadOnlyCollection<Guid> UnitIds, ResidentAccessTypeEnum AccessType, bool IsResponsible)` e `IResidentAuthorizationService` com `GetGrantAsync(ClaimsPrincipal, CancellationToken)` e `CanAccessUnitAsync(ClaimsPrincipal, Guid unitId, CancellationToken)`.

As unidades vêm de `ResidentUnitLinks` filtradas por `IsActive`, `StartsAt <= agora` e (`EndsAt` nulo ou `> agora`). O morador precisa estar `IsActive`; se `Temporary`, `Expire` precisa ser futuro.

- [ ] **Step 1: Escrever os testes que falham**

Usar `Microsoft.EntityFrameworkCore.InMemory` se já disponível no projeto de teste; caso contrário, extrair a regra de filtragem para um método estático puro e testá-la sem banco. **Verificar qual das duas opções o projeto suporta antes de escrever, e reportar.**

Casos obrigatórios:
- vínculo ativo e vigente → unidade incluída;
- vínculo `IsActive = false` → excluída;
- vínculo com `EndsAt` no passado → excluída;
- vínculo com `StartsAt` no futuro → excluída;
- morador `IsActive = false` → grant nulo;
- morador `Temporary` com `Expire` passado → grant nulo;
- `CanAccessUnitAsync` para unidade de outro morador → `false`;
- principal sem `principal_type = resident` → grant nulo.

- [ ] **Steps 2-4:** RED, implementar, GREEN.

- [ ] **Step 5:** registrar como `AddScoped<IResidentAuthorizationService, ResidentAuthorizationService>()`.

- [ ] **Step 6:** build, suíte, commit.

---

## Task 4: Refresh tokens

**Files:**
- Create: `CondotifyAPI.Domain/DTO/Users/RefreshTokenDTO.cs`
- Create: `CondotifyAPI.Infrastructure/ContextConfiguration/User/RefreshTokenConfiguration.cs`
- Create: `CondotifyAPI/Services/Security/RefreshTokenService.cs`
- Modify: `CondotifyAPI.Infrastructure/DatabaseContext` (novo `DbSet`)
- Create: migração
- Test: `CondotifyAPI.Tests/RefreshTokenServiceTests.cs`

**Interfaces:**
- Produces: `IRefreshTokenService` com `IssueAsync(Guid subjectId, string subjectType, string deviceLabel, string ip, CancellationToken)`, `RotateAsync(string presentedToken, CancellationToken)`, `RevokeAsync(string presentedToken, CancellationToken)`, `RevokeAllAsync(Guid subjectId, string subjectType, CancellationToken)`, `ListAsync(Guid subjectId, string subjectType, CancellationToken)`.

Colunas: `Id`, `SubjectId`, `SubjectType`, `TokenHash`, `ExpiresAt`, `CreatedAt`, `RevokedAt`, `ReplacedByHash`, `DeviceLabel`, `CreatedIp`. Índice único em `TokenHash`.

Regras não negociáveis:
- O token é aleatório de 256 bits, devolvido uma única vez, **gravado apenas como SHA-256**, seguindo o padrão de `RegistrationInvite.TokenHash`.
- Validade de 60 dias.
- `RotateAsync` revoga o apresentado e emite outro.
- **Detecção de reuso:** se o token apresentado já estiver revogado, revogar **toda a cadeia** daquele sujeito. Este é o sinal clássico de roubo.

- [ ] **Step 1: Escrever os testes que falham**

Casos obrigatórios, com destaque para os dois últimos:
- emissão devolve um token que valida;
- o valor devolvido **não** aparece na coluna do banco (só o hash);
- token expirado não rotaciona;
- token revogado não rotaciona;
- **rotação invalida o token anterior**;
- **apresentar um token já revogado revoga toda a cadeia do sujeito**;
- `RevokeAllAsync` revoga tudo daquele sujeito e nada de outro.

- [ ] **Steps 2-4:** RED, implementar, GREEN.

- [ ] **Step 5: Migração** — gerar, **ler o arquivo**, confirmar que só cria a tabela nova e não toca nenhuma existente, aplicar.

- [ ] **Step 6:** build, suíte, commit.

---

## Task 5: Definir senha no convite

**Files:**
- Modify: `CondotifyAPI/Controllers/PublicRegistrationController.cs`
- Modify: `CondotifyAPI/Data/...` (o DTO `CompleteRegistrationInviteIn`)
- Modify: `CondotifyAPI.Infrastructure/ContextConfiguration/Resident/ResidentAccessConfiguration.cs` (comentário)
- Test: `CondotifyAPI.Tests/RegistrationInvitePasswordTests.cs`

Hoje o convite completa o cadastro e **não cria acesso nenhum**. Passa a exigir `Password`, validá-la com `PasswordPolicy` e gravá-la com `IPasswordHasher<ResidentAccess>`.

**A task é ponta a ponta**, porque a única via de cadastro é a página web:

1. `CondotifyAPI/Data/...` — `CompleteRegistrationInviteIn` ganha `Password`.
2. `PublicRegistrationController` — valida com `PasswordPolicy`, grava o hash.
3. `Condotify.Contracts/LicenseManagementViewModels.cs:933` — `CompleteRegistrationInviteViewModel` ganha `Password` e `PasswordConfirmation`.
4. `Condotify/Components/Pages/RegistrationInvite.razor` — dois campos de senha, com confirmação, e validação no cliente antes de enviar.

O campo em `Condotify.Contracts` serve também ao SP-4: o aplicativo terá a mesma tela de conclusão de convite e usará o mesmo contrato.

- [ ] **Step 1:** teste que falha se a coluna `Password` contiver a senha original após completar o convite. Este teste é a rede de segurança contra regressão para texto claro.

- [ ] **Steps 2-4:** RED, implementar, GREEN.

- [ ] **Step 5:** build, suíte, commit.

---

## Task 6: Login de morador

**Files:**
- Create: `CondotifyAPI/Controllers/ResidentAuthController.cs`
- Create: `CondotifyAPI/Data/Login/ResidentAuthDtos.cs`
- Test: `CondotifyAPI.Tests/ResidentLoginTests.cs`

`POST /api/auth/resident/login` — `[AllowAnonymous]`, `[EnableRateLimiting("login")]`. Resolve o morador por e-mail, verifica a senha com o hasher, resolve a licença pela unidade primária, emite access + refresh.

Resposta idêntica para e-mail inexistente e senha errada. Atualiza `LastAccess`.

- [ ] Testes: credencial válida devolve par de tokens; e-mail inexistente e senha errada produzem **a mesma** resposta; morador inativo é recusado; morador sem senha definida é recusado.

- [ ] Build, suíte, commit.

---

## Task 7: Sessão — refresh, logout, dispositivos

**Files:**
- Create: `CondotifyAPI/Controllers/SessionController.cs`
- Modify: `CondotifyAPI/Controllers/AuthController.cs` (login da equipe passa a devolver refresh)

Rotas: `POST /api/auth/refresh` (anônima), `POST /api/auth/logout`, `POST /api/auth/logout/all`, `GET /api/auth/sessions`.

`refresh` aceita token de qualquer `SubjectType` e emite o access token do tipo correspondente.

- [ ] Testes: refresh válido devolve par novo; refresh reapresentado após rotação é recusado **e revoga a cadeia**; logout impede refresh subsequente; `sessions` lista só as do próprio sujeito.

- [ ] Build, suíte, commit.

---

## Task 8: Recuperação e troca de senha

**Files:**
- Modify: `CondotifyAPI/Controllers/ResidentAuthController.cs`
- Create: entidade e migração de token de recuperação

`forgot` **sempre 202**. Token de uso único, hash em banco, 30 minutos. `reset` consome e define. `change` exige o morador autenticado e a senha atual.

Envio pelo `AlertNotificationChannelSender`. Sem SMTP configurado, continua 202 e registra em log — o comportamento visível não pode denunciar a configuração.

- [ ] Testes: `forgot` responde 202 para e-mail inexistente **e** existente, com a mesma forma; token usado duas vezes falha na segunda; token expirado falha; senha nova passa por `PasswordPolicy`; trocar a senha revoga todos os refresh do morador.

- [ ] Build, suíte, commit.

---

## Task 9: Adaptar a web ao refresh

**Files:**
- Modify: `Condotify/Controllers/LoginController.cs`

Sem esta task a web passa a deslogar de hora em hora. O `LoginController` guarda o refresh token no cookie, junto ao access token, e o `ClaimsSessionContextProvider` (do SP-0) precisa de um caminho para renovar quando o access expira.

**Esta é a única task do SP-2 que toca o projeto `Condotify`.** Apresentar o desenho antes de implementar: guardar refresh em claim de cookie tem implicações — o cookie já é `HttpOnly` e cifrado por data protection, o que é aceitável, mas merece decisão explícita.

- [ ] Build, suíte, smoke manual do login web.

---

## Task 10: Verificação integrada

- [ ] `POST /api/auth/resident/login` com um morador real do banco, após definir senha por convite.
- [ ] **Token de morador recusado em rota da equipe** — testar contra `GET /api/access/licenses`, esperar `403`.
- [ ] Token da equipe recusado em rota de morador.
- [ ] Rotação de refresh e detecção de reuso, ponta a ponta.
- [ ] Vínculo de unidade revogado deixa de valer **sem** novo login.
- [ ] Registrar o que não foi verificado.

O segundo item é o mais importante do sub-projeto inteiro.
