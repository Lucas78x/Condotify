# SP-0 — Contratos Compartilhados — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extrair de `Condotify` os contratos e o cliente HTTP para bibliotecas compartilhadas, sem alterar o comportamento da versão web, para que o app MAUI (SP-4) os consuma sem duplicação.

**Architecture:** Três bibliotecas `net8.0` novas — `Condotify.Contracts` (ViewModels e DTOs), `Condotify.ApiClient` (`CondotifyApiClient`, `ApiResult<T>` e a abstração de sessão) e `Condotify.UI` (tema MudBlazor único). Os namespaces `Condotify.Models`, `Condotify.Out` e `Condotify.Services` são preservados, então nenhum arquivo `.razor` precisa mudar. A dependência de `AuthenticationStateProvider` dentro de `CondotifyApiClient` é substituída pela interface `ISessionContextProvider`, cuja implementação web replica exatamente o comportamento atual e cuja implementação MAUI (SP-4) lerá do `SecureStorage`.

**Tech Stack:** .NET 8, ASP.NET Core Blazor Server, MudBlazor 9.7.0, QRCoder 1.8.0, xUnit 2.5.3.

Spec: [SP-0 Design](../specs/2026-07-31-sp0-contratos-compartilhados-design.md) · Roadmap: [Condotify Mobile](../specs/2026-07-31-mobile-roadmap-design.md)

## Global Constraints

- Todos os projetos novos usam `TargetFramework` **net8.0**, com `<Nullable>enable</Nullable>` e `<ImplicitUsings>enable</ImplicitUsings>`, iguais aos projetos existentes.
- **Nenhum arquivo `.razor` pode ser modificado**, exceto `Condotify/Components/Layout/MainLayout.razor` e `Condotify/Components/Layout/PublicLayout.razor`, e nesses apenas a linha do tema e o bloco `@code`.
- **Nenhuma alteração em `CondotifyAPI`, `CondotifyAPI.Domain`, `CondotifyAPI.Infrastructure` ou `CondotifyAPI.Tests`.**
- Namespaces preservados na movimentação: `Condotify.Models`, `Condotify.Out`, `Condotify.Services`. Não renomear.
- **Nenhum projeto novo pode usar `Microsoft.NET.Sdk.Web` nem declarar `<FrameworkReference Include="Microsoft.AspNetCore.App" />`.** Esse framework não existe em Android/iOS e tornaria a biblioteca inutilizável pelo app MAUI no SP-4.
- Usar `git mv` (não copiar e apagar) para preservar o histórico dos arquivos.
- Versões de pacote fixas conforme já usadas na solution: `MudBlazor` 9.7.0, `QRCoder` 1.8.0, `xunit` 2.5.3, `Microsoft.NET.Test.Sdk` 17.8.0, `coverlet.collector` 6.0.0. Para os pacotes novos, as versões abaixo foram verificadas como existentes no NuGet — **não existe 8.0.24 para os pacotes `Microsoft.AspNetCore.Components.*`**, cuja série 8.0 começa em 8.0.27.
- **Nenhum arquivo `.cshtml` pode ser modificado.** `Condotify/Views/_ViewImports.cshtml:2` já declara `@using Condotify.Models` e `Condotify/Views/Login/Login.cshtml:1` usa `@model Condotify.Models.LoginViewModel` totalmente qualificado. Como o namespace é preservado na movimentação, ambos continuam válidos sem alteração.
- Comandos executados a partir de `D:\repos\Condotify`.
- **Nunca usar `git add -A` nem `git add .`.** O working tree contém alterações locais não relacionadas que devem permanecer não commitadas: `Condotify/Properties/launchSettings.json`, `CondotifyAPI/Properties/launchSettings.json` (modificados) e `contexto.txt` (não rastreado). Sempre listar os caminhos explicitamente no `git add`. Antes de cada commit, conferir com `git status --short` que esses três continuam fora do índice.

### Desvios conscientes em relação ao spec

1. O spec previa `ClaimsAccessTokenProvider` no projeto `Condotify`. O plano renomeia a abstração para `ISessionContextProvider`/`ClaimsSessionContextProvider` (ver correção na Task 3) e a coloca em `Condotify.ApiClient`. Motivo: é o item de risco **Alto** do SP-0 e precisa de teste unitário; mantê-lo na biblioteca permite testá-lo num projeto de teste leve, sem arrastar o app web inteiro. A classe é genuinamente reutilizável por qualquer host Blazor baseado em `ClaimsPrincipal`.
2. O spec previa SDK Razor para `Condotify.ApiClient`. O plano usa `Microsoft.NET.Sdk`, porque o projeto não contém nenhum `.razor` e `IBrowserFile` vem do pacote `Microsoft.AspNetCore.Components.Web`.
3. O spec não mencionava `Condotify/Out/LoginOut.cs`. Ele é contrato de login, será necessário no SP-4 e portanto entra em `Condotify.Contracts`.

---

## File Structure

**Criados:**

| Arquivo | Responsabilidade |
|---|---|
| `Condotify.Contracts/Condotify.Contracts.csproj` | Biblioteca de contratos, sem dependências |
| `Condotify.Contracts/*.cs` (10 arquivos movidos) | ViewModels, catálogos e `LoginOut` |
| `Condotify.ApiClient/Condotify.ApiClient.csproj` | Biblioteca de acesso à API |
| `Condotify.ApiClient/*.cs` (4 arquivos movidos) | `CondotifyApiClient`, `ApiResult`, `FacePhotoProcessor`, `QrCodeRenderer` |
| `Condotify.ApiClient/ISessionContextProvider.cs` | Abstração dos dados da sessão (token + enterprise id) |
| `Condotify.ApiClient/ClaimsSessionContextProvider.cs` | Implementação sobre `ClaimsPrincipal` + constantes dos claims |
| `Condotify.ApiClient.Tests/Condotify.ApiClient.Tests.csproj` | Projeto de teste |
| `Condotify.ApiClient.Tests/ClaimsSessionContextProviderTests.cs` | Testes do item de risco Alto |
| `Condotify.UI/Condotify.UI.csproj` | Biblioteca de UI compartilhada |
| `Condotify.UI/CondotifyTheme.cs` | Tema MudBlazor único, claro e escuro |

**Modificados:**

| Arquivo | Alteração |
|---|---|
| `Condotify.sln` | Adiciona 4 projetos |
| `Condotify/Condotify.csproj` | Adiciona 3 `ProjectReference` |
| `Condotify.ApiClient/CondotifyApiClient.cs` | Troca `AuthenticationStateProvider` por `ISessionContextProvider`; remove a constante do claim |
| `Condotify/Controllers/LoginController.cs:34,157` | `CondotifyApiClient.AccessTokenClaim` → `ClaimsSessionContextProvider.AccessTokenClaim` |
| `Condotify/Controllers/PrivateMediaController.cs:17` | idem |
| `Condotify/Program.cs:30` | Registra `ISessionContextProvider` |
| `Condotify/Components/Layout/MainLayout.razor` | Usa `CondotifyTheme.Default` |
| `Condotify/Components/Layout/PublicLayout.razor` | Usa `CondotifyTheme.Default` |

---

## Task 1: Criar `Condotify.Contracts` e mover os contratos

**Files:**
- Create: `Condotify.Contracts/Condotify.Contracts.csproj`
- Move: `Condotify/Models/*.cs` (9 arquivos) → `Condotify.Contracts/`
- Move: `Condotify/Out/LoginOut.cs` → `Condotify.Contracts/LoginOut.cs`
- Modify: `Condotify.sln`, `Condotify/Condotify.csproj`

**Interfaces:**
- Consumes: nada.
- Produces: namespace `Condotify.Models` com 118 tipos (entre eles `LicenseViewModel`, `LicenseFullViewModel`, `LoginViewModel`, `ChangePasswordViewModel`, `MfaSecurityViewModel`, `OperationalDashboardViewModel`, `AmenityViewModel`, `LicensePermission`, `LicensePermissionCatalog`) e namespace `Condotify.Out` com `LoginOut`.

- [ ] **Step 1: Criar o projeto**

Criar `Condotify.Contracts/Condotify.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
```

Não precisa de nenhum `PackageReference`: `System.ComponentModel.DataAnnotations` e `System.Text.Json`, usados pelos ViewModels, fazem parte do framework base do .NET 8.

- [ ] **Step 2: Mover os arquivos preservando histórico**

```bash
git mv Condotify/Models/AmenityViewModels.cs Condotify.Contracts/AmenityViewModels.cs
git mv Condotify/Models/DeviceCatalog.cs Condotify.Contracts/DeviceCatalog.cs
git mv Condotify/Models/LicenseFullViewModel.cs Condotify.Contracts/LicenseFullViewModel.cs
git mv Condotify/Models/LicenseManagementViewModels.cs Condotify.Contracts/LicenseManagementViewModels.cs
git mv Condotify/Models/LicensePermissionCatalog.cs Condotify.Contracts/LicensePermissionCatalog.cs
git mv Condotify/Models/LicenseViewModel.cs Condotify.Contracts/LicenseViewModel.cs
git mv Condotify/Models/LoginViewModel.cs Condotify.Contracts/LoginViewModel.cs
git mv Condotify/Models/MfaSecurityViewModel.cs Condotify.Contracts/MfaSecurityViewModel.cs
git mv Condotify/Models/OperationalDashboardViewModels.cs Condotify.Contracts/OperationalDashboardViewModels.cs
git mv Condotify/Out/LoginOut.cs Condotify.Contracts/LoginOut.cs
```

Nenhum arquivo tem o conteúdo editado. Os namespaces declarados dentro deles (`Condotify.Models` e `Condotify.Out`) permanecem como estão — é isso que faz `_Imports.razor` continuar funcionando sem alteração.

- [ ] **Step 3: Registrar na solution e referenciar**

```bash
dotnet sln Condotify.sln add Condotify.Contracts/Condotify.Contracts.csproj
dotnet add Condotify/Condotify.csproj reference Condotify.Contracts/Condotify.Contracts.csproj
```

- [ ] **Step 4: Verificar que as pastas antigas sumiram e que as views MVC não foram tocadas**

Run: `git status --short`
Expected: apenas renomeações (`R`) dos 10 arquivos, mais o `.csproj` novo, o `.sln` e o `Condotify.csproj` modificados. As pastas `Condotify/Models/` e `Condotify/Out/` devem estar vazias e não aparecer.

Run: `git status --short -- "*.cshtml"`
Expected: saída vazia. `Login.cshtml` faz o binding de `Condotify.Models.LoginViewModel`, que acabou de mudar de projeto — se o namespace tivesse sido alterado, a view quebraria em runtime, não em build.

- [ ] **Step 5: Build**

Run: `dotnet build Condotify.sln`
Expected: `Build succeeded`, 0 erros. Se aparecer `CS0246` para algum tipo de `Condotify.Models`, significa que um arquivo ficou para trás — conferir `git status`.

- [ ] **Step 6: Commit**

```bash
git add Condotify.Contracts Condotify/Models Condotify/Out Condotify.sln Condotify/Condotify.csproj
git status --short   # confirmar que launchSettings.json e contexto.txt NAO estao no indice
git commit -m "refactor: extract Condotify.Contracts from web project

Moves the 118 view model types and LoginOut into a dependency-free
net8.0 library so the MAUI app can consume them in SP-4. Namespaces
Condotify.Models and Condotify.Out are preserved, so no .razor file
changes."
```

---

## Task 2: Criar `Condotify.ApiClient` e mover o cliente HTTP

**Files:**
- Create: `Condotify.ApiClient/Condotify.ApiClient.csproj`
- Move: `Condotify/Services/*.cs` (4 arquivos) → `Condotify.ApiClient/`
- Modify: `Condotify.sln`, `Condotify/Condotify.csproj`

**Interfaces:**
- Consumes: `Condotify.Models.*` da Task 1.
- Produces: namespace `Condotify.Services` com `CondotifyApiClient` (~130 métodos públicos, todos retornando `Task<ApiResult<T>>`), `ApiResult<T>` (`record` com `Success`, `Value`, `Error`, `StatusCode` e os factories `Ok`/`Fail`), `FacePhotoProcessor` (`IsSupported(IBrowserFile)`, `PrepareAsync(IBrowserFile, CancellationToken)`) e `QrCodeRenderer.ToPngDataUri(string)`.

- [ ] **Step 1: Criar o projeto**

Criar `Condotify.ApiClient/Condotify.ApiClient.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.Components.Authorization" Version="8.0.29" />
    <PackageReference Include="Microsoft.AspNetCore.Components.Web" Version="8.0.29" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" Version="8.0.0" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.1" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="8.0.2" />
    <PackageReference Include="QRCoder" Version="1.8.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Condotify.Contracts\Condotify.Contracts.csproj" />
  </ItemGroup>

</Project>
```

Cada pacote cobre um uso concreto: `Components.Web` traz `IBrowserFile` para o `FacePhotoProcessor`; `Components.Authorization` traz `AuthenticationStateProvider` para a Task 3; `Extensions.Http` traz `IHttpClientFactory`; os outros dois trazem `IConfiguration` e `ILogger<T>`.

Deliberadamente **não** se usa `Microsoft.NET.Sdk.Razor` nem `FrameworkReference` — ver Global Constraints.

> **Nota de execução (2026-07-31).** Duas edições de conteúdo em `CondotifyApiClient.cs` foram inevitáveis ao sair do SDK Web, nenhuma delas alterando comportamento:
>
> 1. Acrescentar `using Microsoft.Extensions.Configuration;` e `using Microsoft.Extensions.Logging;`. O `Microsoft.NET.Sdk.Web` fornece esses dois como *implicit usings*; o SDK de biblioteca não.
> 2. Trocar duas chamadas `ClaimsPrincipal.FindFirstValue(x)` por `FindFirst(x)?.Value`, que é exatamente o corpo do método de extensão. A extensão vive em `Microsoft.AspNetCore.Authentication.Abstractions`, que é assembly do shared framework e **não pode** ser referenciado como pacote autônomo — tentar `dotnet add package` falha com "incompatível com as estruturas". Isso confirma na prática a restrição global sobre `FrameworkReference`.
>
> `Microsoft.Extensions.Logging.Abstractions` ficou em **8.0.3** (e não 8.0.2) por conflito de resolução de versão com os demais pacotes.
>
> As duas chamadas `FindFirst` são temporárias: a Task 3 remove ambas ao introduzir `ISessionContextProvider`.

- [ ] **Step 2: Mover os arquivos**

```bash
git mv Condotify/Services/ApiResult.cs Condotify.ApiClient/ApiResult.cs
git mv Condotify/Services/CondotifyApiClient.cs Condotify.ApiClient/CondotifyApiClient.cs
git mv Condotify/Services/FacePhotoProcessor.cs Condotify.ApiClient/FacePhotoProcessor.cs
git mv Condotify/Services/QrCodeRenderer.cs Condotify.ApiClient/QrCodeRenderer.cs
```

- [ ] **Step 3: Registrar na solution e referenciar**

```bash
dotnet sln Condotify.sln add Condotify.ApiClient/Condotify.ApiClient.csproj
dotnet add Condotify/Condotify.csproj reference Condotify.ApiClient/Condotify.ApiClient.csproj
```

- [ ] **Step 4: Build**

Run: `dotnet build Condotify.sln`
Expected: `Build succeeded`, 0 erros.

- [ ] **Step 5: Commit**

```bash
git add Condotify.ApiClient Condotify/Services Condotify.sln Condotify/Condotify.csproj
git status --short   # confirmar que launchSettings.json e contexto.txt NAO estao no indice
git commit -m "refactor: extract Condotify.ApiClient from web project

Moves CondotifyApiClient, ApiResult, FacePhotoProcessor and
QrCodeRenderer into a net8.0 library. Uses Microsoft.NET.Sdk with
explicit package references rather than the Razor SDK, so the library
carries no Microsoft.AspNetCore.App framework reference and stays
consumable from the MAUI app in SP-4."
```

---

## Task 3: Abstrair a obtenção do token

Esta é a tarefa de risco Alto do SP-0. Uma regressão aqui passa pelo compilador e pelos testes existentes, e só aparece como falha de autenticação em runtime.

> **Correção do plano aplicada em 2026-07-31, durante a execução.** O desenho original previa apenas `IAccessTokenProvider`, cobrindo o token. A Task 2 revelou que `CondotifyApiClient` lê **dois** valores da sessão, não um: além do token em `CreateClientAsync`, o método `CreateLicenseAsync` lê o claim `enterprise_id`. Uma abstração só de token deixaria `CreateLicenseAsync` preso ao `AuthenticationStateProvider` e portanto quebrado no MAUI. A interface passa a se chamar `ISessionContextProvider` e expõe os dois valores — nome que descreve o que ela realmente é.

**Files:**
- Create: `Condotify.ApiClient/ISessionContextProvider.cs`
- Create: `Condotify.ApiClient/ClaimsSessionContextProvider.cs`
- Create: `Condotify.ApiClient.Tests/Condotify.ApiClient.Tests.csproj`
- Create: `Condotify.ApiClient.Tests/ClaimsSessionContextProviderTests.cs`
- Modify: `Condotify.ApiClient/CondotifyApiClient.cs` (usings, campo, construtor, `CreateLicenseAsync`, `CreateClientAsync`)
- Modify: `Condotify/Controllers/LoginController.cs:34,157`
- Modify: `Condotify/Controllers/PrivateMediaController.cs:17`
- Modify: `Condotify/Program.cs:30`
- Modify: `Condotify.sln`

**Interfaces:**
- Consumes: `CondotifyApiClient` da Task 2.
- Produces:
  - `Condotify.Services.ISessionContextProvider` com dois membros:
    - `ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)`
    - `ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default)`
  - `Condotify.Services.ClaimsSessionContextProvider`, implementação de `ISessionContextProvider`, com as constantes públicas `const string AccessTokenClaim = "condotify_access_token"` e `const string EnterpriseIdClaim = "enterprise_id"`, e construtor `ClaimsSessionContextProvider(AuthenticationStateProvider)`.
  - `CondotifyApiClient` passa a receber `ISessionContextProvider` no lugar de `AuthenticationStateProvider` como segundo parâmetro do construtor, e deixa de referenciar `ClaimsPrincipal` por completo. O SP-4 fornecerá uma implementação sobre `SecureStorage`.

- [ ] **Step 1: Criar o projeto de teste**

Criar `Condotify.ApiClient.Tests/Condotify.ApiClient.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" Version="6.0.0" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Condotify.ApiClient\Condotify.ApiClient.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

```bash
dotnet sln Condotify.sln add Condotify.ApiClient.Tests/Condotify.ApiClient.Tests.csproj
```

- [ ] **Step 2: Escrever o teste que falha**

Criar `Condotify.ApiClient.Tests/ClaimsSessionContextProviderTests.cs`:

```csharp
using System.Security.Claims;
using Condotify.Services;
using Microsoft.AspNetCore.Components.Authorization;

namespace Condotify.ApiClient.Tests;

public class ClaimsSessionContextProviderTests
{
    [Fact]
    public async Task GetAccessTokenAsync_ReturnsToken_WhenClaimPresent()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.AccessTokenClaim, "jwt-abc-123"));

        Assert.Equal("jwt-abc-123", await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenClaimMissing()
    {
        var provider = CreateProvider(new Claim(ClaimTypes.Email, "user@condotify.local"));

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenClaimIsWhitespace()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.AccessTokenClaim, "   "));

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessTokenAsync_ReturnsNull_WhenUserIsAnonymous()
    {
        var provider = new ClaimsSessionContextProvider(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(new ClaimsIdentity())));

        Assert.Null(await provider.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsValue_WhenClaimPresent()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.EnterpriseIdClaim, "8f1d0d3e-0000-4a1b-9c2d-000000000001"));

        Assert.Equal("8f1d0d3e-0000-4a1b-9c2d-000000000001", await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsNull_WhenClaimMissing()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.AccessTokenClaim, "jwt-abc-123"));

        Assert.Null(await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public async Task GetEnterpriseIdAsync_ReturnsNull_WhenClaimIsWhitespace()
    {
        var provider = CreateProvider(
            new Claim(ClaimsSessionContextProvider.EnterpriseIdClaim, "   "));

        Assert.Null(await provider.GetEnterpriseIdAsync());
    }

    [Fact]
    public void ClaimNames_KeepTheValuesTheCookieAlreadyStores()
    {
        Assert.Equal("condotify_access_token", ClaimsSessionContextProvider.AccessTokenClaim);
        Assert.Equal("enterprise_id", ClaimsSessionContextProvider.EnterpriseIdClaim);
    }

    private static ClaimsSessionContextProvider CreateProvider(params Claim[] claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsSessionContextProvider(
            new StubAuthenticationStateProvider(new ClaimsPrincipal(identity)));
    }

    private sealed class StubAuthenticationStateProvider(ClaimsPrincipal user) : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(user));
    }
}
```

O último teste é deliberado. As strings `"condotify_access_token"` e `"enterprise_id"` já estão gravadas nos cookies de sessão em produção — `LoginController.CreatePrincipal` emite ambas. Alterá-las desloga todo mundo silenciosamente, ou quebra a criação de licenças. O teste trava os dois valores.

- [ ] **Step 3: Rodar o teste e confirmar que falha**

Run: `dotnet test Condotify.ApiClient.Tests/Condotify.ApiClient.Tests.csproj`
Expected: FALHA de compilação — `CS0246: The type or namespace name 'ClaimsSessionContextProvider' could not be found`.

- [ ] **Step 4: Criar a interface**

Criar `Condotify.ApiClient/ISessionContextProvider.cs`:

```csharp
namespace Condotify.Services;

/// <summary>
/// Fornece os dados da sessao atual usados pelo <see cref="CondotifyApiClient"/>.
/// A web resolve a partir dos claims do cookie de sessao; o aplicativo MAUI
/// resolve a partir do SecureStorage.
/// </summary>
public interface ISessionContextProvider
{
    /// <summary>Token Bearer enviado a API. Null quando nao ha sessao.</summary>
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>Empresa da sessao, exigida ao criar uma licenca. Null quando ausente.</summary>
    ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 5: Criar a implementação sobre claims**

Criar `Condotify.ApiClient/ClaimsSessionContextProvider.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;

namespace Condotify.Services;

/// <summary>
/// Le os dados da sessao dos claims gravados no cookie pelo LoginController.
/// </summary>
public sealed class ClaimsSessionContextProvider : ISessionContextProvider
{
    /// <summary>
    /// Nome do claim que guarda o token. Este valor ja existe nos cookies
    /// emitidos em producao: alteracao invalida as sessoes ativas.
    /// </summary>
    public const string AccessTokenClaim = "condotify_access_token";

    /// <summary>
    /// Nome do claim que guarda a empresa. Emitido por LoginController a
    /// partir do payload do JWT.
    /// </summary>
    public const string EnterpriseIdClaim = "enterprise_id";

    private readonly AuthenticationStateProvider _authenticationStateProvider;

    public ClaimsSessionContextProvider(AuthenticationStateProvider authenticationStateProvider) =>
        _authenticationStateProvider = authenticationStateProvider;

    public ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        ReadClaimAsync(AccessTokenClaim);

    public ValueTask<string?> GetEnterpriseIdAsync(CancellationToken cancellationToken = default) =>
        ReadClaimAsync(EnterpriseIdClaim);

    private async ValueTask<string?> ReadClaimAsync(string claimType)
    {
        var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
        var value = state.User.FindFirst(claimType)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }
}
```

- [ ] **Step 6: Rodar os testes e confirmar que passam**

Run: `dotnet test Condotify.ApiClient.Tests/Condotify.ApiClient.Tests.csproj`
Expected: `Passed! - Failed: 0, Passed: 8`.

- [ ] **Step 7: Trocar a dependência no `CondotifyApiClient`**

Em `Condotify.ApiClient/CondotifyApiClient.cs`, remover estes três `using` do topo do arquivo — nenhum deles continua em uso depois desta etapa:

```csharp
using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;
```

Remover a constante que vive logo após a abertura da classe (ela agora pertence a `ClaimsSessionContextProvider`):

```csharp
    public const string AccessTokenClaim = "condotify_access_token";
```

Substituir o bloco de campos e o construtor por:

```csharp
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ISessionContextProvider _sessionContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CondotifyApiClient> _logger;

    public CondotifyApiClient(
        IHttpClientFactory httpClientFactory,
        ISessionContextProvider sessionContext,
        IConfiguration configuration,
        ILogger<CondotifyApiClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _sessionContext = sessionContext;
        _configuration = configuration;
        _logger = logger;
    }
```

Em `CreateLicenseAsync`, substituir as duas primeiras linhas do corpo:

```csharp
        var user = (await _authenticationStateProvider.GetAuthenticationStateAsync()).User;
        var enterpriseId = user.FindFirst("enterprise_id")?.Value;
```

por:

```csharp
        var enterpriseId = await _sessionContext.GetEnterpriseIdAsync(cancellationToken);
```

O resto do método fica inalterado, incluindo a guarda `if (!Guid.TryParse(enterpriseId, out _))` e a mensagem de erro que ela devolve.

Substituir `CreateClientAsync` por:

```csharp
    private async Task<HttpClient> CreateClientAsync()
    {
        var client = _httpClientFactory.CreateClient();
        var token = await _sessionContext.GetAccessTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
```

A guarda `IsNullOrWhiteSpace` é mantida de propósito, mesmo com o provider já normalizando: assim o comportamento continua idêntico ao atual para qualquer implementação futura de `ISessionContextProvider`.

Ao final desta etapa, `CondotifyApiClient` não referencia mais `ClaimsPrincipal` nem `AuthenticationStateProvider` em lugar nenhum. Confirme com:

Run: `grep -n "ClaimsPrincipal\|AuthenticationStateProvider\|FindFirst" Condotify.ApiClient/CondotifyApiClient.cs`
Expected: saída vazia.

- [ ] **Step 8: Atualizar as três referências à constante**

Em `Condotify/Controllers/LoginController.cs:34`:

```csharp
            var token = User.FindFirstValue(ClaimsSessionContextProvider.AccessTokenClaim);
```

Em `Condotify/Controllers/LoginController.cs:157`:

```csharp
                new(ClaimsSessionContextProvider.AccessTokenClaim, accessToken),
```

Em `Condotify/Controllers/PrivateMediaController.cs:17`:

```csharp
        var token = User.FindFirstValue(ClaimsSessionContextProvider.AccessTokenClaim);
```

Os três arquivos já declaram `using Condotify.Services;`, então nenhum `using` novo é necessário. Aqui `FindFirstValue` continua válido porque estes arquivos vivem no projeto web, que tem o framework do ASP.NET Core — não os altere para `FindFirst`.

- [ ] **Step 9: Registrar no contêiner de DI**

Em `Condotify/Program.cs`, substituir a linha 30:

```csharp
builder.Services.AddScoped<CondotifyApiClient>();
```

por:

```csharp
builder.Services.AddScoped<ISessionContextProvider, ClaimsSessionContextProvider>();
builder.Services.AddScoped<CondotifyApiClient>();
```

`Program.cs` já declara `using Condotify.Services;` na linha 2.

- [ ] **Step 10: Build e testes completos**

Run: `dotnet build Condotify.sln`
Expected: `Build succeeded`, 0 erros.

Run: `dotnet test Condotify.sln`
Expected: todos os testes passam, incluindo os 5 novos e os já existentes de `CondotifyAPI.Tests`.

- [ ] **Step 11: Verificar que nenhum `.razor` foi tocado**

Run: `git status --short -- "*.razor"`
Expected: saída vazia.

- [ ] **Step 12: Commit**

```bash
git add Condotify.ApiClient Condotify.ApiClient.Tests Condotify.sln Condotify/Program.cs Condotify/Controllers/LoginController.cs Condotify/Controllers/PrivateMediaController.cs
git status --short   # confirmar que launchSettings.json e contexto.txt NAO estao no indice
git commit -m "refactor: abstract session lookup behind ISessionContextProvider

CondotifyApiClient no longer depends on AuthenticationStateProvider or
ClaimsPrincipal, so the MAUI app can supply a SecureStorage-backed
implementation in SP-4.

The abstraction covers both session values the client actually reads:
the bearer token in CreateClientAsync and the enterprise_id claim in
CreateLicenseAsync. ClaimsSessionContextProvider replicates the current
cookie-claim lookup and is covered by unit tests, including one that
pins both claim names since changing either would silently invalidate
live sessions or break licence creation."
```

---

## Task 4: Centralizar o tema MudBlazor

**Files:**
- Create: `Condotify.UI/Condotify.UI.csproj`
- Create: `Condotify.UI/CondotifyTheme.cs`
- Modify: `Condotify/Components/Layout/MainLayout.razor:3,54-92`
- Modify: `Condotify/Components/Layout/PublicLayout.razor:3,16-28`
- Modify: `Condotify.sln`, `Condotify/Condotify.csproj`

**Interfaces:**
- Consumes: nada das tasks anteriores.
- Produces: `Condotify.UI.CondotifyTheme.Default`, propriedade estática do tipo `MudBlazor.MudTheme`, com `PaletteLight`, `PaletteDark`, `LayoutProperties` e `Typography` preenchidos.

- [ ] **Step 1: Criar o projeto**

Criar `Condotify.UI/Condotify.UI.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="MudBlazor" Version="9.7.0" />
  </ItemGroup>

</Project>
```

Aqui o SDK Razor é apropriado: o SP-4 adicionará componentes `.razor` compartilhados neste projeto. O MudBlazor é uma Razor Class Library e não traz `FrameworkReference` ao `Microsoft.AspNetCore.App`, então a restrição de compatibilidade com MAUI continua respeitada.

- [ ] **Step 2: Criar o tema**

Criar `Condotify.UI/CondotifyTheme.cs`:

```csharp
using MudBlazor;

namespace Condotify.UI;

/// <summary>
/// Fonte unica do tema visual do Condotify, compartilhada entre o portal web
/// e o aplicativo mobile. Nao definir cores, raios ou tipografia fixos nos
/// componentes: acrescentar aqui.
/// </summary>
public static class CondotifyTheme
{
    public static MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#3156D3",
            Secondary = "#586579",
            Tertiary = "#007C69",
            Success = "#12805A",
            Warning = "#A96300",
            Error = "#BF3548",
            Info = "#176D91",
            Background = "#F3F5F8",
            Surface = "#FFFFFF",
            DrawerBackground = "#FFFFFF",
            DrawerText = "#4E5A6D",
            AppbarBackground = "#FFFFFF",
            AppbarText = "#1C2431",
            TextPrimary = "#1C2431",
            TextSecondary = "#687386",
            LinesDefault = "#DDE3EA"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#7C9CFF",
            Secondary = "#9AA7B8",
            Tertiary = "#3FBFA6",
            Success = "#3DD68C",
            Warning = "#F0A73C",
            Error = "#F2707F",
            Info = "#54B6DC",
            Background = "#14181F",
            Surface = "#1C222B",
            DrawerBackground = "#1C222B",
            DrawerText = "#B7C0CE",
            AppbarBackground = "#1C222B",
            AppbarText = "#E8ECF2",
            TextPrimary = "#E8ECF2",
            TextSecondary = "#9AA7B8",
            LinesDefault = "#2C3542"
        },
        LayoutProperties = new LayoutProperties
        {
            DefaultBorderRadius = "7px",
            DrawerWidthLeft = "256px",
            AppbarHeight = "68px"
        },
        Typography = new Typography
        {
            Default = new DefaultTypography { FontFamily = ["Inter", "Segoe UI", "sans-serif"] },
            H1 = new H1Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontSize = "1.7rem", FontWeight = "700", LineHeight = "1.22" },
            H2 = new H2Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontSize = "1.4rem", FontWeight = "700", LineHeight = "1.3" },
            H5 = new H5Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontSize = "1.05rem", FontWeight = "650" },
            Subtitle1 = new Subtitle1Typography { FontFamily = ["Inter", "Segoe UI", "sans-serif"], FontWeight = "650" }
        }
    };
}
```

O `PaletteLight`, o `LayoutProperties` e a `Typography` são cópia literal de `MainLayout.razor:59-91`. O `PaletteDark` é novo — não existia em lugar nenhum da solution — e mantém as relações de matiz da paleta clara, com o `Primary` clareado de `#3156D3` para `#7C9CFF` para atingir contraste legível sobre `#14181F`.

- [ ] **Step 3: Registrar na solution e referenciar**

```bash
dotnet sln Condotify.sln add Condotify.UI/Condotify.UI.csproj
dotnet add Condotify/Condotify.csproj reference Condotify.UI/Condotify.UI.csproj
```

- [ ] **Step 4: Apontar o `MainLayout` para o tema**

Em `Condotify/Components/Layout/MainLayout.razor`, adicionar no topo do arquivo, antes da linha 1:

```razor
@using Condotify.UI
```

Substituir a linha 3:

```razor
<MudThemeProvider Theme="CondotifyTheme.Default" />
```

No bloco `@code`, remover integralmente a declaração `private readonly MudTheme _theme = new() { ... };` (linhas 57-92). Preservar `_drawerOpen`, `ToggleDrawer()`, `Initials()` e `AccessLabel()` sem alteração.

- [ ] **Step 5: Apontar o `PublicLayout` para o tema**

Em `Condotify/Components/Layout/PublicLayout.razor`, adicionar no topo do arquivo, antes da linha 1:

```razor
@using Condotify.UI
```

Substituir a linha 3:

```razor
<MudThemeProvider Theme="CondotifyTheme.Default" />
```

Remover integralmente o bloco `@code { ... }` (linhas 16-28), que continha apenas a declaração do tema. O arquivo passa a não ter bloco `@code`.

- [ ] **Step 6: Build**

Run: `dotnet build Condotify.sln`
Expected: `Build succeeded`, 0 erros. Se surgir `CS0103: The name 'CondotifyTheme' does not exist`, o `@using Condotify.UI` não foi adicionado ao layout.

- [ ] **Step 7: Confirmar que só os dois layouts mudaram**

Run: `git status --short -- "*.razor"`
Expected: exatamente duas linhas, `M Condotify/Components/Layout/MainLayout.razor` e `M Condotify/Components/Layout/PublicLayout.razor`.

- [ ] **Step 8: Commit**

```bash
git add Condotify.UI Condotify.sln Condotify/Condotify.csproj Condotify/Components/Layout/MainLayout.razor Condotify/Components/Layout/PublicLayout.razor
git status --short   # confirmar que launchSettings.json e contexto.txt NAO estao no indice
git commit -m "refactor: centralize MudBlazor theme in Condotify.UI

MainLayout and PublicLayout each carried their own palette, and the two
had drifted apart on eight properties. Both now use a single
CondotifyTheme.Default built from the MainLayout values, which also adds
the PaletteDark the solution never had. The public invite screen shifts
by a few points of hue as a result."
```

---

## Task 5: Verificação funcional

O compilador não consegue provar que o token continua chegando à API. Esta tarefa fecha essa lacuna.

**Files:** nenhum arquivo alterado. Se um defeito aparecer, ele é corrigido na task de origem.

**Interfaces:**
- Consumes: tudo das Tasks 1 a 4.
- Produces: confirmação de que o SP-0 preservou o comportamento da web.

- [ ] **Step 1: Subir a infraestrutura e a API**

```bash
docker compose up -d postgres
dotnet run --project CondotifyAPI/CondotifyAPI.csproj
```

Expected: a API sobe e responde em `https://localhost:7118`.

- [ ] **Step 2: Subir o portal**

Em outro terminal:

```bash
dotnet run --project Condotify/Condotify.csproj
```

Expected: portal disponível na porta indicada em `Condotify/Properties/launchSettings.json`.

- [ ] **Step 3: Validar o caminho autenticado**

Percorrer, no navegador:

1. `/Login` — a tela carrega com o tema aplicado.
2. Entrar com um usuário válido — o redirecionamento para `/` acontece.
3. O dashboard exibe dados vindos da API, não mensagem de erro.

O passo 3 é o critério que importa: dados no dashboard provam que `ClaimsSessionContextProvider` entregou o token e que o header `Bearer` chegou à API. Se aparecer "Sua sessão expirou. Entre novamente." ou "A API está indisponível", o defeito está na Task 3.

- [ ] **Step 4: Validar um módulo que usa POST e mídia privada**

1. Abrir o workspace de uma licença e navegar até um módulo de dados (Estrutura ou Portaria).
2. Abrir um perfil de pessoa que tenha foto cadastrada.

Isso exercita `PrivateMediaController`, que foi alterado no Step 8 da Task 3. A foto precisa carregar.

- [ ] **Step 5: Conferir a tela pública**

Abrir `/convite/{token}` com um convite de registro válido, ou apenas a rota de convite. Confirmar que a tela renderiza com o tema consolidado. A mudança de matiz esperada está documentada no spec.

- [ ] **Step 6: Rodar a suíte completa uma última vez**

Run: `dotnet test Condotify.sln`
Expected: todos os testes passam.

- [ ] **Step 7: Registrar a conclusão**

```bash
git commit --allow-empty -m "chore: SP-0 verified end to end

Login, dashboard data, private media and the public invite screen all
behave as before the extraction."
```

---

## Estado final esperado

`Condotify.sln` com 9 projetos:

```
Condotify                   (Web, net8.0)  -> Contracts, ApiClient, UI
Condotify.Contracts         (Lib, net8.0)
Condotify.ApiClient         (Lib, net8.0)  -> Contracts
Condotify.ApiClient.Tests   (Test, net8.0) -> ApiClient
Condotify.UI                (RCL, net8.0)
CondotifyAPI                (Web, net8.0)  -> Infrastructure   [intocado]
CondotifyAPI.Domain         (Lib, net8.0)                      [intocado]
CondotifyAPI.Infrastructure (Lib, net8.0)                      [intocado]
CondotifyAPI.Tests          (Test, net8.0) -> CondotifyAPI     [intocado]
```

O SP-1 (gateway de mídia CFTV) começa a partir daqui.
