# SP-0 — Extração de Contratos Compartilhados — Design

Data: 2026-07-31

Parte de: [Condotify Mobile — Roadmap](2026-07-31-mobile-roadmap-design.md)

## Contexto

`Condotify` (Blazor Server) é hoje consumidora HTTP pura da `CondotifyAPI`. Todo o acoplamento passa por dois diretórios:

- `Condotify/Models/` — 118 tipos, 1.663 linhas
- `Condotify/Services/` — `CondotifyApiClient` (1.184 linhas, ~130 métodos), `ApiResult`, `FacePhotoProcessor`, `QrCodeRenderer`

O app MAUI (SP-4) precisa exatamente desses dois conjuntos. Sem extraí-los, o SP-4 duplicaria 118 ViewModels e um cliente HTTP inteiro — e a plataforma passaria a ter três cópias divergentes do modelo de permissões.

### O que torna esta extração barata

`Condotify/Components/_Imports.razor` já contém `@using Condotify.Models` e `@using Condotify.Services`. Fora das próprias pastas, apenas três arquivos `.cs` referenciam esses namespaces: `Condotify/Controllers/LoginController.cs`, `Condotify/Controllers/PrivateMediaController.cs` e `Condotify/Program.cs`.

**Se as bibliotecas novas preservarem os namespaces, nenhum dos 24 arquivos `.razor` (5.199 linhas) precisa mudar.** A extração vira mover arquivo mais adicionar `ProjectReference`.

## Escopo

Dentro do escopo:
- Criar `Condotify.Contracts`, `Condotify.ApiClient` e `Condotify.UI`.
- Mover `Condotify/Models/*` e `Condotify/Services/*` preservando namespaces.
- Substituir a dependência de `AuthenticationStateProvider` em `CondotifyApiClient` por uma abstração `IAccessTokenProvider`.
- Consolidar o tema MudBlazor em fonte única, incluindo `PaletteDark`.

Fora do escopo:
- Unificar os 152 DTOs `*In`/`*Out` da API com os 118 `*ViewModel` da web.
- Renomear `Condotify` para `Condotify.Web`, ou criar `Condotify.Domain` / `.Application` / `.Infrastructure`.
- Mover componentes Razor compartilhados para `Condotify.UI`.
- Qualquer mudança na `CondotifyAPI`.

### Justificativa das exclusões

**Unificação de DTOs:** os pares `*ViewModel` ↔ `*Out` são espelhados à mão. Unificá-los tocaria as 5.199 linhas de Razor para ganhar elegância, contra a instrução explícita de evitar refatoração grande. A duplicação web↔API permanece exatamente como está hoje; o que o SP-0 impede é ela virar tripla.

**Renomeação de projetos:** `Domain`, `Application` e `Infrastructure` já existem como `CondotifyAPI.*`. Renomear é risco puro sem ganho funcional.

**Componentes Razor:** quais componentes servem ao mobile é especulação até o SP-4 existir. `StatTile` e `PageHeader` são candidatos, mas movê-los agora seria adivinhação. Movem-se quando o SP-4 provar a necessidade.

## Projetos novos

Todos `net8.0`. O app MAUI (SP-4) usará `net9.0-android` / `net9.0-ios`, que referenciam bibliotecas `net8.0` sem multi-targeting.

| Projeto | SDK | Conteúdo | Namespace |
|---|---|---|---|
| `Condotify.Contracts` | `Microsoft.NET.Sdk` | os 9 arquivos de `Condotify/Models/` | `Condotify.Models` (preservado) |
| `Condotify.ApiClient` | `Microsoft.NET.Sdk.Razor` | `CondotifyApiClient`, `ApiResult`, `FacePhotoProcessor`, `QrCodeRenderer`, `IAccessTokenProvider` | `Condotify.Services` (preservado) |
| `Condotify.UI` | `Microsoft.NET.Sdk.Razor` | `CondotifyTheme` | `Condotify.UI` |

### Dependências

`Condotify.Contracts` — nenhum pacote. `System.ComponentModel.DataAnnotations` está no framework base.

`Condotify.ApiClient` — SDK Razor, porque `FacePhotoProcessor` usa `IBrowserFile` (`Microsoft.AspNetCore.Components.Forms`). Referencia `Condotify.Contracts`. Precisa de `QRCoder` (usado por `QrCodeRenderer`).

`Condotify.UI` — SDK Razor, pacote `MudBlazor`.

Ambos os SDKs Razor funcionam igualmente em Blazor Server e MAUI Blazor Hybrid.

Grafo resultante:

```
Condotify (Web) ──┬─> Condotify.ApiClient ──> Condotify.Contracts
                  └─> Condotify.UI
```

## Mudança de comportamento: `IAccessTokenProvider`

Esta é a única alteração funcional do SP-0.

Hoje `CondotifyApiClient.CreateClientAsync()` (`Condotify/Services/CondotifyApiClient.cs:1093`) obtém o token de um claim do cookie de sessão:

```csharp
var user = (await _authenticationStateProvider.GetAuthenticationStateAsync()).User;
var token = user.FindFirstValue(AccessTokenClaim);
```

No MAUI não há cookie nem `AuthenticationStateProvider` equivalente. Introduz-se a abstração:

```csharp
namespace Condotify.Services;

public interface IAccessTokenProvider
{
    ValueTask<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
```

Implementações:

- **Web** — `Condotify/Services/ClaimsAccessTokenProvider.cs` (novo, permanece no projeto web): lê `AuthenticationStateProvider` e o claim `condotify_access_token`, replicando o comportamento atual sem alteração.
- **Mobile** — implementação sobre `SecureStorage`, entregue no SP-4.

`CondotifyApiClient` troca a dependência `AuthenticationStateProvider` por `IAccessTokenProvider` e deixa de referenciar `Microsoft.AspNetCore.Components.Authorization`.

A constante `CondotifyApiClient.AccessTokenClaim` migra para `ClaimsAccessTokenProvider`, já que é um detalhe do mecanismo de cookie da web. Três referências passam a apontar para o novo local:

- `Condotify/Controllers/LoginController.cs:34` (leitura do token na validação de sessão)
- `Condotify/Controllers/LoginController.cs:157` (emissão do claim no login)
- `Condotify/Controllers/PrivateMediaController.cs:17` (leitura do token para proxy de mídia privada)

Registro em `Condotify/Program.cs:30`:

```csharp
builder.Services.AddScoped<IAccessTokenProvider, ClaimsAccessTokenProvider>();
builder.Services.AddScoped<CondotifyApiClient>();
```

## Tema centralizado

`MainLayout.razor:57` e `PublicLayout.razor:17` definem paletas separadas e divergentes:

| Propriedade | MainLayout | PublicLayout |
|---|---|---|
| `Success` | `#12805A` | `#16845B` |
| `Warning` | `#A96300` | `#B86B00` |
| `Error` | `#BF3548` | `#C13D4B` |
| `Background` | `#F3F5F8` | `#F4F6F8` |
| `TextPrimary` | `#1C2431` | `#202532` |
| `TextSecondary` | `#687386` | `#697386` |
| `LinesDefault` | `#DDE3EA` | `#E3E7ED` |
| `DefaultBorderRadius` | `7px` | `6px` |

`Condotify.UI/CondotifyTheme.cs` expõe um `MudTheme` estático único, adotando os valores do `MainLayout` — é a tela de operação real, e a divergência do `PublicLayout` não aparenta ser intencional. Acrescenta o `PaletteDark` que hoje não existe em lugar nenhum, exigido pelo requisito de tema escuro no mobile.

Ambos os layouts passam a usar `Theme="CondotifyTheme.Default"`.

**Impacto visual:** oito propriedades mudam na tela pública de convite (`RegistrationInvite.razor`, via `PublicLayout`). São diferenças de poucos pontos de matiz, imperceptíveis lado a lado, mas é a única mudança visível do SP-0 na versão web e fica registrada aqui.

## Riscos

| Risco | Grau | Mitigação |
|---|---|---|
| Regressão silenciosa na obtenção do token | **Alto** | `ClaimsAccessTokenProvider` replica a lógica atual sem alteração. Teste de unidade cobrindo claim presente, ausente e vazio. Smoke manual do fluxo autenticado. |
| Quebra de build por `using` faltante | Baixo | Namespaces preservados. Apenas `Program.cs` ganha uma linha. |
| Mudança visual na tela pública | Baixo | Documentada acima. Reversível trocando oito constantes. |
| `CondotifyAPI.Tests` afetado | Nenhum | Não referencia `Condotify`. |
| Conflito de merge com trabalho em andamento na web | Baixo | Movimentação de arquivos inteiros, sem edição de conteúdo, exceto nos três pontos descritos. |

## Estratégia de migração

Seis passos, cada um um commit isolado com build verde:

1. Criar os três `.csproj` e adicioná-los ao `Condotify.sln`.
2. Mover `Condotify/Models/*.cs` para `Condotify.Contracts/`. Adicionar `ProjectReference`. **Build.**
3. Mover `Condotify/Services/*.cs` para `Condotify.ApiClient/`. Adicionar `ProjectReference`. **Build.**
4. Introduzir `IAccessTokenProvider` e `ClaimsAccessTokenProvider`; ajustar `CondotifyApiClient`, `LoginController` e `Program.cs`. **Build + testes.**
5. Criar `CondotifyTheme` com `PaletteLight` e `PaletteDark`; apontar os dois layouts. **Build.**
6. Executar a web e validar o fluxo login → dashboard → workspace de licença.

Reversível em qualquer passo.

## Verificação

Critérios de aceitação:

1. `dotnet build Condotify.sln` conclui sem erros novos.
2. `dotnet test` permanece verde.
3. Nenhum arquivo `.razor` foi modificado, exceto `MainLayout.razor` e `PublicLayout.razor` (apenas a linha do tema).
4. Testes de unidade novos para `ClaimsAccessTokenProvider` cobrindo claim presente, ausente e vazio.
5. Smoke manual: login, dashboard carregando dados da API, e abertura do workspace de uma licença.

O critério 5 é indispensável: o compilador não consegue provar que o token continua chegando à API. É a única regressão possível que o build não pega.
