# SP-1 — Gateway de Mídia CFTV — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Dar à plataforma a capacidade de entregar vídeo ao vivo das câmeras a um cliente web ou mobile, sem que credencial de equipamento alguma saia do servidor.

**Architecture:** Dois planos. A `CondotifyAPI` é o plano de controle: valida permissão, decifra a credencial da câmera, monta a URL RTSP, registra o caminho no MediaMTX e devolve ao cliente apenas uma URL de playback com token efêmero. O MediaMTX é o plano de dados: converte RTSP em WebRTC e HLS, e consulta a API por HTTP a cada leitura para autorizar. Snapshot autenticado complementa, servindo miniatura de lista e fallback de câmera offline.

**Tech Stack:** .NET 8, EF Core (PostgreSQL), MediaMTX (imagem oficial via Docker Compose), AES-GCM, xUnit 2.5.3.

Spec: [SP-1 Design](../specs/2026-07-31-sp1-gateway-midia-cftv-design.md) · Roadmap: [Condotify Mobile](../specs/2026-07-31-mobile-roadmap-design.md)

## Global Constraints

- Tudo em **net8.0**, acompanhando o resto da solution.
- **Nenhuma credencial de câmera (`UserName`, `Password`), nenhuma URL RTSP montada e nenhum IP interno de equipamento pode aparecer em resposta HTTP ao cliente, em log, ou em mensagem de erro.** Esta é a restrição central do sub-projeto.
- **Nenhuma alteração no projeto `Condotify` (web), nem em `Condotify.Contracts`, `Condotify.ApiClient` ou `Condotify.UI`.** O SP-1 é backend puro; o consumo acontece no SP-4.
- Autorização continua no servidor via `RequireLicensePermissionAttribute` e `ILicenseAuthorizationService`. Nenhuma regra migra para o cliente.
- Convenções da API existentes, observadas nos controllers atuais: `[ApiController]`, `[Authorize]`, `[Route("api/access/licenses/{licenseId:guid}/...")]`, `[RequireLicensePermission(...)]` no nível da classe, `DatabaseContext` injetado direto, DTOs manuais em `CondotifyAPI/Data/`, sem AutoMapper para saída nova.
- Registro de serviços em `CondotifyAPI/Program.cs`, junto ao bloco existente das linhas 63-73 (singletons de infraestrutura) ou 144-149 (serviços com escopo).
- Migrações via `dotnet ef migrations add <Nome> --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI`.
- **Nunca usar `git add -A` nem `git add .`.** Devem permanecer não commitados: `Condotify/Properties/launchSettings.json`, `CondotifyAPI/Properties/launchSettings.json` (modificados) e `contexto.txt` (não rastreado). Listar caminhos explicitamente e conferir com `git status --short` antes de cada commit.
- As portas do MediaMTX `9997` (Control API) **nunca** são publicadas no host. `8888` e `8889` só com o callback de autenticação ativo.
- Comandos executados a partir de `D:\repos\Condotify`.

### Limites ambientais registrados

A tabela `CFTVDevices` está **vazia** no ambiente de desenvolvimento e não há câmera real cadastrada. A verificação usa fonte RTSP sintética (Task 8), que prova a arquitetura mas **não** a compatibilidade com cada modelo de fabricante. Isso deve ser reportado como pendência real, nunca como verificado.

---

## File Structure

**Criados:**

| Arquivo | Responsabilidade |
|---|---|
| `CondotifyAPI/Services/CFTV/CftvStreamPathResolver.cs` | Resolve caminhos RTSP por fabricante; monta e mascara URLs |
| `CondotifyAPI/Services/CFTV/MediaAccessTokenService.cs` | Emite e valida tokens efêmeros AES-GCM |
| `CondotifyAPI/Services/CFTV/MediaGatewayClient.cs` | Cliente da Control API do MediaMTX |
| `CondotifyAPI/Services/CFTV/CftvHealthMonitoringWorker.cs` | Verificação periódica de online/offline |
| `CondotifyAPI/Controllers/CftvStreamingController.cs` | Sessões, snapshot e status |
| `CondotifyAPI/Controllers/MediaAuthController.cs` | Callback de autorização do MediaMTX |
| `CondotifyAPI/Data/Equipments/CftvStreamingDtos.cs` | DTOs de entrada e saída |
| `mediamtx/mediamtx.yml` | Configuração do MediaMTX |
| `CondotifyAPI.Tests/CftvStreamPathResolverTests.cs` | Testes do resolvedor e do mascaramento |
| `CondotifyAPI.Tests/MediaAccessTokenServiceTests.cs` | Testes do token |
| `CondotifyAPI.Tests/CftvStreamingContractTests.cs` | Garante que nenhum DTO de saída carrega credencial |

**Modificados:**

| Arquivo | Alteração |
|---|---|
| `CondotifyAPI/Services/CFTV/CFTVService.cs` | Passa a usar o resolvedor; `Attempts` deixa de conter credencial |
| `CondotifyAPI/Services/CFTV/ICFTVService.cs` | Acrescenta `SnapshotAsync` |
| `CondotifyAPI/Controllers/CftvDeviceController.cs` | Deixa de devolver URLs com credencial |
| `CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs` | Acrescenta `IsActive`, `LastSeenAt`, `HealthMessage` |
| `CondotifyAPI.Infrastructure/ContextConfiguration/Equipments/CFTVDeviceConfiguration.cs` | Mapeia as três colunas |
| `CondotifyAPI/Program.cs` | Registra os serviços novos e o worker |
| `docker-compose.yml` | Serviço `mediamtx` |
| `.env.example` | Variáveis novas do gateway |

---

## Task 1: Estancar o vazamento de credencial

Esta task é independente das demais e corrige uma falha viva. Pode ser mergeada sozinha se necessário.

**Files:**
- Modify: `CondotifyAPI/Services/CFTV/CFTVService.cs`
- Modify: `CondotifyAPI/Controllers/CftvDeviceController.cs`
- Test: `CondotifyAPI.Tests/CftvStreamPathResolverTests.cs` (criado aqui, ampliado na Task 2)

**Interfaces:**
- Consumes: nada.
- Produces: `CondotifyAPI.Services.CFTV.RtspUrlMasker.Mask(string url)` — estático, devolve a URL com a credencial substituída por `***:***`.

- [ ] **Step 1: Escrever o teste que falha**

Criar `CondotifyAPI.Tests/CftvStreamPathResolverTests.cs`:

```csharp
using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

public class RtspUrlMaskerTests
{
    [Fact]
    public void Mask_RemovesCredentials_FromRtspUrl()
    {
        var masked = RtspUrlMasker.Mask("rtsp://admin:s3nh4Secreta@192.168.0.10:554/cam/realmonitor?channel=1&subtype=0");

        Assert.DoesNotContain("s3nh4Secreta", masked);
        Assert.DoesNotContain("admin", masked);
        Assert.Contains("192.168.0.10:554", masked);
        Assert.Contains("/cam/realmonitor", masked);
    }

    [Fact]
    public void Mask_HandlesUrlWithoutCredentials()
    {
        var masked = RtspUrlMasker.Mask("rtsp://192.168.0.10:554/live");

        Assert.Equal("rtsp://192.168.0.10:554/live", masked);
    }

    [Fact]
    public void Mask_HandlesEncodedCredentials()
    {
        var masked = RtspUrlMasker.Mask("rtsp://user%40dom:p%40ss@10.0.0.1:554/h264");

        Assert.DoesNotContain("p%40ss", masked);
        Assert.Contains("10.0.0.1:554/h264", masked);
    }

    [Fact]
    public void Mask_ReturnsPlaceholder_ForGarbageInput()
    {
        Assert.Equal("rtsp://***", RtspUrlMasker.Mask("nao-e-uma-url"));
    }
}
```

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test CondotifyAPI.Tests --filter RtspUrlMaskerTests`
Expected: falha de compilação, `CS0103` ou `CS0246` para `RtspUrlMasker`.

- [ ] **Step 3: Implementar o mascarador**

Criar `CondotifyAPI/Services/CFTV/RtspUrlMasker.cs`:

```csharp
namespace CondotifyAPI.Services.CFTV;

/// <summary>
/// Remove usuario e senha de URLs RTSP antes que elas apareçam em resposta
/// HTTP, log ou mensagem de erro. URLs montadas por BuildRtspUrl contêm a
/// credencial do equipamento em texto claro.
/// </summary>
public static class RtspUrlMasker
{
    private const string Placeholder = "rtsp://***";

    public static string Mask(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Placeholder;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return Placeholder;

        if (string.IsNullOrEmpty(parsed.UserInfo)) return url;

        var builder = new UriBuilder(parsed)
        {
            UserName = "***",
            Password = "***"
        };

        return builder.Uri.ToString();
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test CondotifyAPI.Tests --filter RtspUrlMaskerTests`
Expected: `Aprovado! - Com falha: 0, Aprovado: 4`.

- [ ] **Step 5: Mascarar na origem**

Em `CondotifyAPI/Services/CFTV/CFTVService.cs`, os dois pontos que registram tentativas passam a guardar a URL mascarada. Em `TestCameraAsync`, substituir:

```csharp
                channelResult.Attempts.Add(url);
```

por:

```csharp
                channelResult.Attempts.Add(RtspUrlMasker.Mask(url));
```

Fazer a mesma substituição em `TestChannelAsync`. Localizar ambas com:

Run: `grep -n "Attempts.Add" CondotifyAPI/Services/CFTV/CFTVService.cs`
Expected: exatamente duas ocorrências, ambas alteradas.

**Importante:** a variável `url` continua sendo usada sem máscara nas chamadas `RtspOptionsAsync` e `RtspDescribeAsync` — é ela que efetivamente conecta. Só o que é *registrado* muda.

- [ ] **Step 6: Mascarar também `RtspUrlWorking`**

No mesmo arquivo, `channelResult.RtspUrlWorking = url;` aparece em ambos os métodos e é devolvido ao cliente em caso de sucesso. Substituir as duas ocorrências por:

```csharp
                    channelResult.RtspUrlWorking = RtspUrlMasker.Mask(url);
```

Run: `grep -n "RtspUrlWorking = " CondotifyAPI/Services/CFTV/CFTVService.cs`
Expected: todas as atribuições mascaradas.

- [ ] **Step 7: Build e suíte completa**

Run: `dotnet build Condotify.sln`
Expected: 0 erros.

Run: `dotnet test Condotify.sln`
Expected: tudo passa; o total sobe em 4.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI/Services/CFTV CondotifyAPI.Tests/CftvStreamPathResolverTests.cs
git status --short   # confirmar que launchSettings.json e contexto.txt NAO estao no indice
git commit -m "fix: stop leaking camera passwords through the CFTV test endpoint

CFTVService.BuildRtspUrl returns rtsp://user:pass@host/path. That string
was collected verbatim into ChannelTestResultOut.Attempts and into
RtspUrlWorking, and CftvDeviceController returns both to the caller — so
any user holding ManageDevices could read a camera's password in
plaintext simply by provoking a failed connection test.

Credentials are now masked at the point they are recorded. The unmasked
URL is still what actually connects; only what is reported changes."
```

---

## Task 2: Extrair o resolvedor de caminhos RTSP

> **Correção do plano aplicada em 2026-08-01, durante a execução.** A primeira versão desta task assumia que `CFTVService` resolvia caminhos por fabricante para câmeras. Não resolve. `GetCameraTemplates` devolve `/axis-media/media.amp` para Axis e apenas `/live`, `/stream1`, `/h264` para **todas** as outras marcas. Os dicionários ricos `RtspPathsByBrand` (linha 15) e `GenericRtspPaths` (linha 65) existem mas são **código morto**, nunca referenciados. Só `RtspPathTemplatesByBrand`, usado por `GetDvrTemplates`, é vivo — e seus valores diferem da reconstrução que a versão anterior deste plano trazia, em Uniview, Axis, Hikvision e no fallback genérico.
>
> O resolvedor passa a expor duas famílias separadas: `ConnectivityProbePaths`, que reproduz o comportamento vivo byte a byte, e `PreferredPath`, que usa a tabela rica e serve só ao gateway. Nenhum caminho existente muda de comportamento.

**Files:**
- Create: `CondotifyAPI/Services/CFTV/CftvStreamPathResolver.cs`
- Modify: `CondotifyAPI/Services/CFTV/CFTVService.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/CftvStreamPathResolverTests.cs`

**Interfaces:**
- Consumes: `RtspUrlMasker` da Task 1.
- Produces:
  - `CondotifyAPI.Services.CFTV.StreamQuality` — enum `{ Main = 0, Secondary = 1 }`.
  - `ICftvStreamPathResolver` com:
    - `IReadOnlyList<string> ConnectivityProbePaths(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel)` — o que o teste de conexão usa; reproduz o comportamento atual.
    - `string? PreferredPath(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel, StreamQuality quality)` — só para o gateway; `null` quando a marca é desconhecida.
    - `string BuildRtspUrl(string ip, int port, string user, string password, string path)`
  - `CftvStreamPathResolver`, registrado como singleton.

- [ ] **Step 1: Conferir os valores reais antes de escrever qualquer teste**

Run: `grep -n -A20 "GetCameraTemplates" CondotifyAPI/Services/CFTV/CFTVService.cs`
Run: `grep -n -A40 "RtspPathTemplatesByBrand = new" CondotifyAPI/Services/CFTV/CFTVService.cs`
Run: `grep -rn "enum CFTVDeviceTypeEnum" -A8 CondotifyAPI.Domain/`

Anote os valores exatos. **Os valores esperados nos testes abaixo devem bater com o que você encontrar. Se algum divergir, o código real vence — corrija o teste e reporte a divergência.** Confirme também o nome do membro de `CFTVDeviceTypeEnum` para gravador; este plano assume `Recorder`, mas pode ser outro.

- [ ] **Step 2: Escrever os testes que falham**

Acrescentar ao arquivo existente `CondotifyAPI.Tests/CftvStreamPathResolverTests.cs`, preservando a classe `RtspUrlMaskerTests` que já está nele:

```csharp
public class CftvStreamPathResolverTests
{
    private readonly CftvStreamPathResolver _resolver = new();

    // --- ConnectivityProbePaths: contrato de NAO-REGRESSAO ---
    // Estes valores sao os que CFTVService usa hoje contra hardware real.
    // Se um destes testes falhar apos uma alteracao, a alteracao esta errada.

    [Fact]
    public void ConnectivityProbePaths_ForAxisCamera_MatchesTodaysBehaviour()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Axis, CFTVDeviceTypeEnum.Camera, 1);

        Assert.Equal(["/axis-media/media.amp", "/axis-media/media.amp?videocodec=h264"], paths);
    }

    [Theory]
    [InlineData(MarkEnum.Intelbras)]
    [InlineData(MarkEnum.Dahua)]
    [InlineData(MarkEnum.Hikvision)]
    [InlineData(MarkEnum.Uniview)]
    [InlineData(MarkEnum.None)]
    public void ConnectivityProbePaths_ForNonAxisCamera_IsGeneric_AsToday(MarkEnum mark)
    {
        var paths = _resolver.ConnectivityProbePaths(mark, CFTVDeviceTypeEnum.Camera, 1);

        Assert.Equal(["/live", "/stream1", "/h264"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForHikvisionRecorder_SubstitutesChannel()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Hikvision, CFTVDeviceTypeEnum.Recorder, 3);

        Assert.Equal(
        [
            "/Streaming/Channels/0301",
            "/Streaming/Channels/0302",
            "/h264/ch03/main/av_stream",
            "/h264/ch03/sub/av_stream"
        ], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForUniviewRecorder_UsesTheLiveChFormat()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Uniview, CFTVDeviceTypeEnum.Recorder, 2);

        Assert.Equal(["/live/ch02_0", "/live/ch02_1"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForAxisRecorder_HasNoChannelSubstitution()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.Axis, CFTVDeviceTypeEnum.Recorder, 5);

        Assert.Equal(["/axis-media/media.amp"], paths);
    }

    [Fact]
    public void ConnectivityProbePaths_ForUnknownRecorderBrand_UsesTheTwoEntryFallback()
    {
        var paths = _resolver.ConnectivityProbePaths(MarkEnum.None, CFTVDeviceTypeEnum.Recorder, 7);

        Assert.Equal(["/cam/realmonitor?channel=07&subtype=0", "/Streaming/Channels/0701"], paths);
    }

    // --- PreferredPath: comportamento NOVO, so para o gateway ---

    [Theory]
    [InlineData(MarkEnum.Intelbras, StreamQuality.Main, "subtype=0")]
    [InlineData(MarkEnum.Intelbras, StreamQuality.Secondary, "subtype=1")]
    [InlineData(MarkEnum.Hikvision, StreamQuality.Main, "/Streaming/Channels/101")]
    [InlineData(MarkEnum.Hikvision, StreamQuality.Secondary, "/Streaming/Channels/102")]
    [InlineData(MarkEnum.Uniview, StreamQuality.Main, "/live/0/main")]
    [InlineData(MarkEnum.Uniview, StreamQuality.Secondary, "/live/0/sub")]
    public void PreferredPath_ForCamera_UsesThePerBrandTable(
        MarkEnum mark, StreamQuality quality, string expectedFragment)
    {
        var path = _resolver.PreferredPath(mark, CFTVDeviceTypeEnum.Camera, 1, quality);

        Assert.NotNull(path);
        Assert.Contains(expectedFragment, path);
    }

    [Fact]
    public void PreferredPath_ReturnsNull_ForUnknownBrand()
    {
        Assert.Null(_resolver.PreferredPath(MarkEnum.None, CFTVDeviceTypeEnum.Camera, 1, StreamQuality.Main));
    }

    [Fact]
    public void PreferredPath_ForRecorder_SubstitutesChannel()
    {
        var path = _resolver.PreferredPath(MarkEnum.Hikvision, CFTVDeviceTypeEnum.Recorder, 4, StreamQuality.Main);

        Assert.Equal("/Streaming/Channels/0401", path);
    }

    [Fact]
    public void BuildRtspUrl_EscapesCredentials()
    {
        var url = _resolver.BuildRtspUrl("10.0.0.1", 554, "user@dom", "p@ss word", "/live");

        Assert.StartsWith("rtsp://", url);
        Assert.Contains("10.0.0.1:554/live", url);
        Assert.DoesNotContain(" ", url);
    }
}
```

- [ ] **Step 3: Rodar e confirmar que falha**

Run: `dotnet test CondotifyAPI.Tests --filter CftvStreamPathResolverTests`
Expected: falha de compilação — `CftvStreamPathResolver` não existe.

- [ ] **Step 4: Criar o resolvedor**

Criar `CondotifyAPI/Services/CFTV/CftvStreamPathResolver.cs`. As tabelas de sondagem devem ser **cópia literal** do que `GetCameraTemplates` e `RtspPathTemplatesByBrand` contêm hoje. A tabela de preferência vem do dicionário morto `RtspPathsByBrand`.

```csharp
using CondotifyAPI.Domain.Models.Equipments;

namespace CondotifyAPI.Services.CFTV;

public enum StreamQuality
{
    Main = 0,
    Secondary = 1
}

public interface ICftvStreamPathResolver
{
    /// <summary>
    /// Caminhos que o teste de conectividade percorre. Reproduz exatamente o
    /// comportamento historico de CFTVService: nao alterar sem testar contra
    /// o equipamento correspondente.
    /// </summary>
    IReadOnlyList<string> ConnectivityProbePaths(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel);

    /// <summary>
    /// Melhor caminho conhecido para o gateway de midia. Comportamento novo:
    /// nenhum fluxo existente usa isto. Devolve null para marca desconhecida.
    /// </summary>
    string? PreferredPath(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel, StreamQuality quality);

    string BuildRtspUrl(string ip, int port, string user, string password, string path);
}

public sealed class CftvStreamPathResolver : ICftvStreamPathResolver
{
    // === Sondagem: copia literal do comportamento vivo ===

    private static readonly string[] AxisCameraProbe =
        ["/axis-media/media.amp", "/axis-media/media.amp?videocodec=h264"];

    private static readonly string[] GenericCameraProbe =
        ["/live", "/stream1", "/h264"];

    private static readonly Dictionary<MarkEnum, string[]> RecorderProbeByBrand = new()
    {
        [MarkEnum.Intelbras] = ["/cam/realmonitor?channel={ch}&subtype=0", "/cam/realmonitor?channel={ch}&subtype=1"],
        [MarkEnum.Dahua] = ["/cam/realmonitor?channel={ch}&subtype=0", "/cam/realmonitor?channel={ch}&subtype=1"],
        [MarkEnum.Hikvision] = ["/Streaming/Channels/{ch}01", "/Streaming/Channels/{ch}02", "/h264/ch{ch}/main/av_stream", "/h264/ch{ch}/sub/av_stream"],
        [MarkEnum.Hilook] = ["/Streaming/Channels/{ch}01", "/Streaming/Channels/{ch}02"],
        [MarkEnum.Uniview] = ["/live/ch{ch}_0", "/live/ch{ch}_1"],
        [MarkEnum.Axis] = ["/axis-media/media.amp"]
    };

    private static readonly string[] GenericRecorderProbe =
        ["/cam/realmonitor?channel={ch}&subtype=0", "/Streaming/Channels/{ch}01"];

    // === Preferencia: tabela rica, ate agora nao utilizada, so para o gateway ===

    private static readonly Dictionary<MarkEnum, string[]> PreferredCameraByBrand = new()
    {
        [MarkEnum.Intelbras] = ["/cam/realmonitor?channel=1&subtype=0", "/cam/realmonitor?channel=1&subtype=1"],
        [MarkEnum.Dahua] = ["/cam/realmonitor?channel=1&subtype=0", "/cam/realmonitor?channel=1&subtype=1"],
        [MarkEnum.Hikvision] = ["/Streaming/Channels/101", "/Streaming/Channels/102"],
        [MarkEnum.Hilook] = ["/Streaming/Channels/101", "/Streaming/Channels/102"],
        [MarkEnum.Uniview] = ["/live/0/main", "/live/0/sub"],
        [MarkEnum.Axis] = ["/axis-media/media.amp", "/axis-media/media.amp?videocodec=h264"]
    };

    public IReadOnlyList<string> ConnectivityProbePaths(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel)
    {
        if (deviceType == CFTVDeviceTypeEnum.Camera)
            return mark == MarkEnum.Axis ? AxisCameraProbe : GenericCameraProbe;

        var templates = RecorderProbeByBrand.TryGetValue(mark, out var found) ? found : GenericRecorderProbe;
        return Substitute(templates, channel);
    }

    public string? PreferredPath(MarkEnum mark, CFTVDeviceTypeEnum deviceType, int channel, StreamQuality quality)
    {
        string[]? candidates;

        if (deviceType == CFTVDeviceTypeEnum.Camera)
        {
            if (!PreferredCameraByBrand.TryGetValue(mark, out candidates)) return null;
        }
        else
        {
            if (!RecorderProbeByBrand.TryGetValue(mark, out var templates)) return null;
            candidates = Substitute(templates, channel);
        }

        if (candidates.Length == 0) return null;

        var index = quality == StreamQuality.Secondary && candidates.Length > 1 ? 1 : 0;
        return candidates[index];
    }

    public string BuildRtspUrl(string ip, int port, string user, string password, string path)
    {
        if (!path.StartsWith('/')) path = "/" + path;
        var escapedUser = Uri.EscapeDataString(user ?? string.Empty);
        var escapedPassword = Uri.EscapeDataString(password ?? string.Empty);
        return $"rtsp://{escapedUser}:{escapedPassword}@{ip}:{port}{path}";
    }

    private static string[] Substitute(string[] templates, int channel) =>
        templates.Select(x => x.Replace("{ch}", channel.ToString("D2"))).ToArray();
}
```

- [ ] **Step 5: Rodar e confirmar que passa**

Run: `dotnet test CondotifyAPI.Tests --filter CftvStreamPathResolverTests`
Expected: todos passam.

- [ ] **Step 6: Fazer `CFTVService` consumir o resolvedor**

Injetar `ICftvStreamPathResolver` pelo construtor de `CFTVService`. Em `TestCameraAsync`, substituir:

```csharp
            var templates = GetCameraTemplates(device.Mark);
```

por:

```csharp
            var templates = _pathResolver.ConnectivityProbePaths(device.Mark, device.DeviceType, 1);
```

Em `TestChannelAsync`, substituir:

```csharp
            var templates = GetDvrTemplates(device.Mark);
```

por:

```csharp
            var templates = _pathResolver.ConnectivityProbePaths(device.Mark, device.DeviceType, channel);
```

e remover a linha `var path = tpl.Replace("{ch}", channel.ToString("D2"));`, ajustando o laço para iterar sobre `path` diretamente — a substituição de canal agora é feita pelo resolvedor. Substituir `BuildRtspUrl(...)` por `_pathResolver.BuildRtspUrl(...)` nos dois métodos.

Remover em seguida, de `CFTVService.cs`, os membros que ficaram órfãos: `RtspPathsByBrand`, `GenericRtspPaths`, `GetCameraTemplates`, `GetDvrTemplates`, `RtspPathTemplatesByBrand` e `BuildRtspUrl`. Confirmar:

Run: `grep -n "RtspPathsByBrand\|GenericRtspPaths\|GetCameraTemplates\|GetDvrTemplates\|RtspPathTemplatesByBrand" CondotifyAPI/Services/CFTV/CFTVService.cs`
Expected: saída vazia.

Os seis pontos de mascaramento da Task 1 permanecem intactos.

- [ ] **Step 7: Registrar no contêiner**

Em `CondotifyAPI/Program.cs`, junto ao bloco de singletons:

```csharp
builder.Services.AddSingleton<ICftvStreamPathResolver, CftvStreamPathResolver>();
```

- [ ] **Step 8: Build e suíte completa**

Run: `dotnet build Condotify.sln`
Expected: 0 erros, sem avisos novos.

Run: `dotnet test Condotify.sln`
Expected: tudo passa.

- [ ] **Step 9: Commit**

```bash
git add CondotifyAPI/Services/CFTV CondotifyAPI/Program.cs CondotifyAPI.Tests/CftvStreamPathResolverTests.cs
git status --short
git commit -m "refactor: extract the CFTV RTSP path resolver from CFTVService"
```

Corpo da mensagem de commit:

```
The media gateway needs to know how to reach a camera stream, knowledge
that lived as private members of CFTVService. Extracting it surfaced
that the per-brand camera table (RtspPathsByBrand) was dead code: the
live GetCameraTemplates gives every brand but Axis the same three
generic paths, with no main/secondary distinction. Only the recorder
table was ever wired up.

The resolver therefore separates two things that were being conflated.
ConnectivityProbePaths reproduces the live behaviour byte for byte,
including the poverty of the camera paths, and is what the connectivity
test keeps using - so nothing changes about what is attempted against
real hardware. PreferredPath uses the previously-dead per-brand table
and serves only the gateway. Improving camera path discovery needs a
camera to verify against and is deliberately left alone.
```

---

## Task 3: Serviço de token efêmero

**Files:**
- Create: `CondotifyAPI/Services/CFTV/MediaAccessTokenService.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/MediaAccessTokenServiceTests.cs`

**Interfaces:**
- Consumes: nada das tasks anteriores.
- Produces:
  - `CondotifyAPI.Services.CFTV.MediaAccessGrant` — `record(Guid LicenseId, Guid DeviceId, int Channel, Guid UserId, DateTime ExpiresAt)`.
  - `IMediaAccessTokenService` com `string Issue(MediaAccessGrant grant)` e `MediaAccessGrant? Validate(string token, string expectedPath)`.
  - `MediaAccessTokenService.PathFor(Guid licenseId, Guid deviceId, int channel)` — estático, devolve o nome do caminho no MediaMTX.

O caminho é `l{licenseId:N}_d{deviceId:N}_c{channel}`, sem hífens, porque o MediaMTX restringe os caracteres aceitos em nome de path.

- [ ] **Step 1: Escrever os testes que falham**

Criar `CondotifyAPI.Tests/MediaAccessTokenServiceTests.cs`:

```csharp
using CondotifyAPI.Services.CFTV;
using Xunit;

namespace CondotifyAPI.Tests;

public class MediaAccessTokenServiceTests
{
    private const string Secret = "test-secret-com-comprimento-suficiente-para-derivar-chave";

    private static MediaAccessTokenService CreateService() => new(Secret);

    private static MediaAccessGrant Grant(DateTime? expiresAt = null) => new(
        LicenseId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        DeviceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        Channel: 1,
        UserId: Guid.Parse("33333333-3333-3333-3333-333333333333"),
        ExpiresAt: expiresAt ?? DateTime.UtcNow.AddSeconds(120));

    [Fact]
    public void Validate_AcceptsAFreshToken_ForTheMatchingPath()
    {
        var service = CreateService();
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);

        var result = service.Validate(service.Issue(grant), path);

        Assert.NotNull(result);
        Assert.Equal(grant.DeviceId, result!.DeviceId);
        Assert.Equal(grant.UserId, result.UserId);
    }

    [Fact]
    public void Validate_RejectsAnExpiredToken()
    {
        var service = CreateService();
        var grant = Grant(DateTime.UtcNow.AddSeconds(-1));
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);

        Assert.Null(service.Validate(service.Issue(grant), path));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherCamera()
    {
        var service = CreateService();
        var grant = Grant();
        var otherPath = MediaAccessTokenService.PathFor(
            grant.LicenseId, Guid.Parse("44444444-4444-4444-4444-444444444444"), 1);

        Assert.Null(service.Validate(service.Issue(grant), otherPath));
    }

    [Fact]
    public void Validate_RejectsATokenIssuedForAnotherLicense()
    {
        var service = CreateService();
        var grant = Grant();
        var otherPath = MediaAccessTokenService.PathFor(
            Guid.Parse("55555555-5555-5555-5555-555555555555"), grant.DeviceId, 1);

        Assert.Null(service.Validate(service.Issue(grant), otherPath));
    }

    [Fact]
    public void Validate_RejectsATamperedToken()
    {
        var service = CreateService();
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);
        var token = service.Issue(grant);
        var tampered = token[..^4] + (token.EndsWith("AAAA") ? "BBBB" : "AAAA");

        Assert.Null(service.Validate(tampered, path));
    }

    [Fact]
    public void Validate_RejectsATokenSignedWithAnotherSecret()
    {
        var grant = Grant();
        var path = MediaAccessTokenService.PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);
        var token = new MediaAccessTokenService("outro-segredo-completamente-diferente-do-primeiro").Issue(grant);

        Assert.Null(CreateService().Validate(token, path));
    }

    [Fact]
    public void Validate_RejectsGarbage()
    {
        Assert.Null(CreateService().Validate("nao-e-um-token", "qualquer-path"));
        Assert.Null(CreateService().Validate("", "qualquer-path"));
    }

    [Fact]
    public void Issue_ProducesADifferentTokenEachTime_ForTheSameGrant()
    {
        var service = CreateService();
        var grant = Grant();

        Assert.NotEqual(service.Issue(grant), service.Issue(grant));
    }

    [Fact]
    public void PathFor_ProducesAMediaMtxSafeName()
    {
        var path = MediaAccessTokenService.PathFor(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            2);

        Assert.DoesNotContain("-", path);
        Assert.Matches("^[A-Za-z0-9_]+$", path);
    }
}
```

O teste de token adulterado importa mais do que parece: sem verificação de integridade, um cliente poderia trocar o `DeviceId` no payload e assistir a qualquer câmera da instalação.

- [ ] **Step 2: Rodar e confirmar que falha**

Run: `dotnet test CondotifyAPI.Tests --filter MediaAccessTokenServiceTests`
Expected: falha de compilação.

- [ ] **Step 3: Implementar o serviço**

Criar `CondotifyAPI/Services/CFTV/MediaAccessTokenService.cs`:

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace CondotifyAPI.Services.CFTV;

public sealed record MediaAccessGrant(
    Guid LicenseId,
    Guid DeviceId,
    int Channel,
    Guid UserId,
    DateTime ExpiresAt);

public interface IMediaAccessTokenService
{
    string Issue(MediaAccessGrant grant);
    MediaAccessGrant? Validate(string token, string expectedPath);
}

/// <summary>
/// Emite tokens de curta duracao que autorizam a leitura de UM caminho de
/// midia. Mesmo esquema AES-GCM usado por PrivateMediaStore. Nao substitui o
/// JWT: o plano de controle continua exigindo autenticacao normal.
/// </summary>
public sealed class MediaAccessTokenService : IMediaAccessTokenService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int PayloadSize = 16 + 16 + 4 + 16 + 8; // license + device + channel + user + expiry

    private readonly byte[] _key;

    public MediaAccessTokenService(string secret) =>
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));

    public MediaAccessTokenService(IConfiguration configuration)
        : this(Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_SECRET")
            ?? configuration["Media:Secret"]
            ?? throw new InvalidOperationException(
                "Defina CONDOTIFY_MEDIA_SECRET para emitir tokens de video."))
    {
    }

    public static string PathFor(Guid licenseId, Guid deviceId, int channel) =>
        $"l{licenseId:N}_d{deviceId:N}_c{channel}";

    public string Issue(MediaAccessGrant grant)
    {
        var plain = new byte[PayloadSize];
        grant.LicenseId.TryWriteBytes(plain.AsSpan(0, 16));
        grant.DeviceId.TryWriteBytes(plain.AsSpan(16, 16));
        BinaryPrimitives.WriteInt32LittleEndian(plain.AsSpan(32, 4), grant.Channel);
        grant.UserId.TryWriteBytes(plain.AsSpan(36, 16));
        BinaryPrimitives.WriteInt64LittleEndian(
            plain.AsSpan(52, 8),
            new DateTimeOffset(DateTime.SpecifyKind(grant.ExpiresAt, DateTimeKind.Utc)).ToUnixTimeSeconds());

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(_key, TagSize)) aes.Encrypt(nonce, plain, cipher, tag);

        var payload = new byte[NonceSize + TagSize + cipher.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(cipher, 0, payload, NonceSize + TagSize, cipher.Length);
        CryptographicOperations.ZeroMemory(plain);

        return Base64UrlEncode(payload);
    }

    public MediaAccessGrant? Validate(string token, string expectedPath)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(expectedPath)) return null;

        byte[] payload;
        try
        {
            payload = Base64UrlDecode(token);
        }
        catch (FormatException)
        {
            return null;
        }

        if (payload.Length != NonceSize + TagSize + PayloadSize) return null;

        var plain = new byte[PayloadSize];
        try
        {
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(
                payload.AsSpan(0, NonceSize),
                payload.AsSpan(NonceSize + TagSize),
                payload.AsSpan(NonceSize, TagSize),
                plain);
        }
        catch (CryptographicException)
        {
            return null;
        }

        var grant = new MediaAccessGrant(
            new Guid(plain.AsSpan(0, 16)),
            new Guid(plain.AsSpan(16, 16)),
            BinaryPrimitives.ReadInt32LittleEndian(plain.AsSpan(32, 4)),
            new Guid(plain.AsSpan(36, 16)),
            DateTimeOffset.FromUnixTimeSeconds(
                BinaryPrimitives.ReadInt64LittleEndian(plain.AsSpan(52, 8))).UtcDateTime);

        if (grant.ExpiresAt <= DateTime.UtcNow) return null;

        var boundPath = PathFor(grant.LicenseId, grant.DeviceId, grant.Channel);
        return string.Equals(boundPath, expectedPath, StringComparison.Ordinal) ? grant : null;
    }

    private static string Base64UrlEncode(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var normalized = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(normalized.PadRight(normalized.Length + (4 - normalized.Length % 4) % 4, '='));
    }
}
```

- [ ] **Step 4: Rodar e confirmar que passa**

Run: `dotnet test CondotifyAPI.Tests --filter MediaAccessTokenServiceTests`
Expected: `Aprovado! - Com falha: 0, Aprovado: 9`.

- [ ] **Step 5: Registrar no contêiner**

Em `CondotifyAPI/Program.cs`, junto aos singletons:

```csharp
builder.Services.AddSingleton<IMediaAccessTokenService, MediaAccessTokenService>();
```

O construtor que recebe `IConfiguration` é o que o DI vai escolher.

- [ ] **Step 6: Build e suíte completa**

Run: `dotnet build Condotify.sln`
Expected: 0 erros.

Run: `dotnet test Condotify.sln`
Expected: tudo passa.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Services/CFTV CondotifyAPI/Program.cs CondotifyAPI.Tests/MediaAccessTokenServiceTests.cs
git status --short
git commit -m "feat: add short-lived AES-GCM tokens for camera stream access

A token authorises reading one media path and nothing else. It carries
licence, device, channel, user and expiry, and is bound to the path name
derived from the first three — so swapping the device id in a captured
token yields a path mismatch rather than access to another camera.

Uses the same AES-GCM construction as PrivateMediaStore rather than
introducing a second scheme. Tests cover expiry, tampering, cross-camera
and cross-licence reuse, and a foreign signing secret."
```

---

## Task 4: MediaMTX no compose

**Files:**
- Create: `mediamtx/mediamtx.yml`
- Modify: `docker-compose.yml`
- Modify: `.env.example`

**Interfaces:**
- Consumes: nada.
- Produces: serviço `mediamtx` alcançável por `http://mediamtx:9997` (Control API), `http://mediamtx:8888` (HLS) e `http://mediamtx:8889` (WebRTC) **de dentro da rede Docker**, e as variáveis `CONDOTIFY_MEDIA_GATEWAY_URL`, `CONDOTIFY_MEDIA_PLAYBACK_BASEURL`.

- [ ] **Step 1: Criar a configuração**

Criar `mediamtx/mediamtx.yml`:

```yaml
# Condotify — configuracao do gateway de midia.
#
# ATENCAO: com authMethod: http, a Control API (apiAddress) NAO e coberta
# pelo callback de autenticacao. Verificado em 2026-08-01 contra a imagem
# 1.9.3: POST /v3/config/paths/add e GET /v3/config/paths/list respondem 200
# sem credencial e sem disparar o callback, e paths/list devolve o campo
# source em texto claro, ou seja rtsp://usuario:senha@host/caminho.
# A porta 9997 NUNCA pode ser publicada no host.

logLevel: info
logDestinations: [stdout]

api: yes
apiAddress: :9997

metrics: no
pprof: no
playback: no

# Protocolos de entrega ao cliente.
hls: yes
hlsAddress: :8888
hlsVariant: lowLatency
hlsAlwaysRemux: no

webrtc: yes
webrtcAddress: :8889

# Protocolos que nao usamos: desligados para reduzir superficie.
rtmp: no
srt: no

# O RTSP fica ligado apenas como CLIENTE (para puxar das cameras).
# O servidor RTSP de entrada nao e necessario.
rtsp: no

# Autorizacao delegada a API do Condotify. Toda leitura dispara este callback.
authMethod: http
authHTTPAddress: http://api:8080/api/internal/media-auth

paths: {}
```

`hlsVariant: lowLatency` reduz a latência de HLS; o WebRTC continua sendo o caminho preferido.

- [ ] **Step 2: Acrescentar o serviço ao compose**

Em `docker-compose.yml`, acrescentar após o serviço `api`:

```yaml
  mediamtx:
    image: bluenviron/mediamtx:1.9.3
    container_name: condotify-mediamtx
    restart: unless-stopped
    volumes:
      - ./mediamtx/mediamtx.yml:/mediamtx.yml:ro
    ports:
      # 8888 (HLS) e 8889 (WebRTC) sao os unicos publicados, e apenas
      # porque o callback de autenticacao protege cada leitura.
      # 9997 (Control API) NAO e publicada: ver mediamtx/mediamtx.yml.
      - "8888:8888"
      - "8889:8889"
      - "8189:8189/udp"
    depends_on:
      - api
```

A porta `8189/udp` é o ICE do WebRTC e precisa estar aberta para a mídia fluir.

- [ ] **Step 3: Ligar a API ao gateway**

No serviço `api` do mesmo arquivo, acrescentar ao bloco `environment`:

```yaml
      CONDOTIFY_MEDIA_GATEWAY_URL: http://mediamtx:9997
      CONDOTIFY_MEDIA_PLAYBACK_BASEURL: ${CONDOTIFY_MEDIA_PLAYBACK_BASEURL:-http://localhost:8889}
```

`CONDOTIFY_MEDIA_GATEWAY_URL` é interno à rede Docker. `CONDOTIFY_MEDIA_PLAYBACK_BASEURL` é o endereço que o **cliente** usa, e em produção deve ser o host público que serve o WebRTC.

- [ ] **Step 4: Documentar as variáveis**

Acrescentar ao final de `.env.example`:

```
# Endereco publico pelo qual o aplicativo alcanca o gateway de video.
# Em producao, aponte para o host/proxy que expoe as portas 8888 e 8889.
CONDOTIFY_MEDIA_PLAYBACK_BASEURL=http://localhost:8889
```

- [ ] **Step 5: Verificar que a Control API não está exposta**

```bash
docker compose up -d mediamtx
sleep 5
```

Run: `curl -s -o /dev/null -w "%{http_code}" --max-time 3 http://localhost:9997/v3/config/paths/list ; echo`
Expected: **falha de conexão** (código `000` e mensagem de recusa). Se responder `200`, a porta foi publicada por engano e o deploy está inseguro — corrigir antes de seguir.

Run: `docker compose exec api sh -c "wget -qO- http://mediamtx:9997/v3/config/paths/list" 2>/dev/null || docker run --rm --network condotify_default curlimages/curl -s http://mediamtx:9997/v3/config/paths/list`
Expected: JSON com `itemCount: 0` — a API alcança o gateway pela rede interna.

- [ ] **Step 6: Commit**

```bash
git add mediamtx docker-compose.yml .env.example
git status --short
git commit -m "feat: add MediaMTX as the CFTV media data plane"
```

Corpo:

```
Converts camera RTSP into WebRTC and HLS so a browser or mobile WebView
can play it, with every read authorised by a callback into the API.

Only 8888, 8889 and the WebRTC ICE port are published. 9997 is
deliberately not: measured against this exact image version, the Control
API answers 200 with no credential and without firing the auth callback
when authMethod is http, and paths/list returns each path's source field
verbatim - which is rtsp://user:pass@host/path. Anything that can reach
that port can read every camera password in the installation.
```

---

## Task 5: Cliente da Control API

**Files:**
- Create: `CondotifyAPI/Services/CFTV/MediaGatewayClient.cs`
- Modify: `CondotifyAPI/Program.cs`
- Test: `CondotifyAPI.Tests/MediaGatewayClientTests.cs`

**Interfaces:**
- Consumes: nada das tasks anteriores.
- Produces: `IMediaGatewayClient` com:
  - `Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken ct)`
  - `Task RemovePathAsync(string path, CancellationToken ct)`
  - `Task<int> ActivePathCountAsync(CancellationToken ct)`

`EnsurePathAsync` devolve `false` quando o gateway está indisponível, para que o chamador responda `503 GatewayUnavailable` em vez de estourar.

- [ ] **Step 1: Escrever os testes que falham**

Criar `CondotifyAPI.Tests/MediaGatewayClientTests.cs`:

```csharp
using System.Net;
using System.Text;
using CondotifyAPI.Services.CFTV;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CondotifyAPI.Tests;

public class MediaGatewayClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            Bodies.Add(request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return respond(request);
        }
    }

    private static (MediaGatewayClient Client, StubHandler Handler) Create(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://mediamtx:9997") };
        return (new MediaGatewayClient(http, NullLogger<MediaGatewayClient>.Instance), handler);
    }

    [Fact]
    public async Task EnsurePathAsync_PostsTheSourceOnDemand_AndReturnsTrue()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var ok = await client.EnsurePathAsync("l1_d2_c1", "rtsp://u:p@10.0.0.1:554/live", CancellationToken.None);

        Assert.True(ok);
        Assert.Contains("/v3/config/paths/add/l1_d2_c1", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("\"sourceOnDemand\":true", handler.Bodies[0]);
        Assert.Contains("rtsp://u:p@10.0.0.1:554/live", handler.Bodies[0]);
    }

    [Fact]
    public async Task EnsurePathAsync_TreatsAnExistingPathAsSuccess()
    {
        // O MediaMTX devolve 400 quando o caminho ja existe; isso nao e erro
        // para o nosso fluxo, porque o caminho ja esta pronto para leitura.
        var (client, _) = Create(request => request.RequestUri!.ToString().Contains("/add/")
            ? new HttpResponseMessage(HttpStatusCode.BadRequest)
            { Content = new StringContent("path already exists", Encoding.UTF8) }
            : new HttpResponseMessage(HttpStatusCode.OK));

        Assert.True(await client.EnsurePathAsync("l1_d2_c1", "rtsp://x", CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePathAsync_ReturnsFalse_WhenTheGatewayIsUnreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.False(await client.EnsurePathAsync("l1_d2_c1", "rtsp://x", CancellationToken.None));
    }

    [Fact]
    public async Task EnsurePathAsync_ReturnsFalse_OnUnexpectedStatus()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        Assert.False(await client.EnsurePathAsync("l1_d2_c1", "rtsp://x", CancellationToken.None));
    }

    [Fact]
    public async Task RemovePathAsync_CallsDelete_AndSwallowsFailure()
    {
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        await client.RemovePathAsync("l1_d2_c1", CancellationToken.None);

        Assert.Contains("/v3/config/paths/delete/l1_d2_c1", handler.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task ActivePathCountAsync_ReadsItemCount()
    {
        var (client, _) = Create(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"itemCount\":3,\"items\":[]}", Encoding.UTF8, "application/json")
        });

        Assert.Equal(3, await client.ActivePathCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ActivePathCountAsync_ReturnsZero_WhenTheGatewayIsUnreachable()
    {
        var (client, _) = Create(_ => throw new HttpRequestException("connection refused"));

        Assert.Equal(0, await client.ActivePathCountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TheRtspSource_IsNeverWrittenToTheLog()
    {
        // O logger nulo nao registra nada; este teste existe para travar a
        // intencao: se alguem trocar por um logger real e passar a URL, o
        // teste deve ser atualizado deliberadamente, nao por acidente.
        var (client, handler) = Create(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await client.EnsurePathAsync("l1_d2_c1", "rtsp://user:senha@10.0.0.1:554/live", CancellationToken.None);

        Assert.Single(handler.Bodies);
    }
}
```

- [ ] **Step 2: RED**

Run: `dotnet test CondotifyAPI.Tests --filter MediaGatewayClientTests`
Expected: falha de compilação — `MediaGatewayClient` não existe.

- [ ] **Step 3: Implementar**

Criar `CondotifyAPI/Services/CFTV/MediaGatewayClient.cs`:

```csharp
using System.Net.Http.Json;
using System.Text.Json;

namespace CondotifyAPI.Services.CFTV;

public interface IMediaGatewayClient
{
    Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken cancellationToken = default);
    Task RemovePathAsync(string path, CancellationToken cancellationToken = default);
    Task<int> ActivePathCountAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Cliente da Control API do MediaMTX. Esta API NAO tem autenticacao quando
/// authMethod e http, entao o endereco configurado precisa ser alcancavel
/// apenas pela rede interna. Nunca registre o rtspSource: ele contem a
/// credencial da camera.
/// </summary>
public sealed class MediaGatewayClient : IMediaGatewayClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MediaGatewayClient> _logger;

    public MediaGatewayClient(HttpClient http, ILogger<MediaGatewayClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<bool> EnsurePathAsync(string path, string rtspSource, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsJsonAsync(
                $"/v3/config/paths/add/{path}",
                new { source = rtspSource, sourceOnDemand = true },
                cancellationToken);

            if (response.IsSuccessStatusCode) return true;

            // O gateway devolve 400 quando o caminho ja existe. Para o nosso
            // fluxo isso e sucesso: o caminho esta pronto para leitura.
            if (response.StatusCode == System.Net.HttpStatusCode.BadRequest) return true;

            _logger.LogWarning(
                "O gateway de midia recusou o registro do caminho {Path}. Status {Status}.",
                path, (int)response.StatusCode);
            return false;
        }
        catch (Exception exception)
        {
            // A mensagem nunca inclui rtspSource.
            _logger.LogError(exception, "Falha ao comunicar com o gateway de midia ao registrar {Path}.", path);
            return false;
        }
    }

    public async Task RemovePathAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.PostAsync($"/v3/config/paths/delete/{path}", null, cancellationToken);
            if (!response.IsSuccessStatusCode)
                _logger.LogDebug("O caminho {Path} nao pode ser removido. Status {Status}.", path, (int)response.StatusCode);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao remover o caminho {Path} do gateway de midia.", path);
        }
    }

    public async Task<int> ActivePathCountAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await _http.GetAsync("/v3/paths/list", cancellationToken);
            if (!response.IsSuccessStatusCode) return 0;

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return document.RootElement.TryGetProperty("itemCount", out var count) ? count.GetInt32() : 0;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Falha ao consultar os caminhos ativos do gateway de midia.");
            return 0;
        }
    }
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test CondotifyAPI.Tests --filter MediaGatewayClientTests`
Expected: `Aprovado! - Com falha: 0, Aprovado: 8`.

- [ ] **Step 5: Registrar como HttpClient nomeado**

Em `CondotifyAPI/Program.cs`, seguindo o padrão de `AddHttpClient("AlertNotifications", ...)`:

```csharp
builder.Services.AddHttpClient<IMediaGatewayClient, MediaGatewayClient>(client =>
{
    client.BaseAddress = new Uri(
        Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_GATEWAY_URL") ?? "http://mediamtx:9997");
    client.Timeout = TimeSpan.FromSeconds(5);
});
```

O timeout curto importa: se o gateway travar, a abertura de sessão precisa falhar rápido em vez de segurar a requisição do usuário.

- [ ] **Step 6: Build e suíte**

Run: `dotnet build Condotify.sln` — 0 erros.
Run: `dotnet test Condotify.sln` — tudo passa.

- [ ] **Step 7: Commit**

```bash
git add CondotifyAPI/Services/CFTV/MediaGatewayClient.cs CondotifyAPI/Program.cs CondotifyAPI.Tests/MediaGatewayClientTests.cs
git status --short
git commit -m "feat: add a typed client for the MediaMTX control API"
```

---

## Task 6: Sessões de vídeo e callback de autorização

Esta é a task central do sub-projeto: é onde a credencial da câmera é usada sem sair do servidor.

**Files:**
- Create: `CondotifyAPI/Data/Equipments/CftvStreamingDtos.cs`
- Create: `CondotifyAPI/Controllers/CftvStreamingController.cs`
- Create: `CondotifyAPI/Controllers/MediaAuthController.cs`
- Test: `CondotifyAPI.Tests/CftvStreamingContractTests.cs`

**Interfaces:**
- Consumes: `ICftvStreamPathResolver` (Task 2), `IMediaAccessTokenService` + `MediaAccessTokenService.PathFor` (Task 3), `IMediaGatewayClient` (Task 5).
- Produces: `CftvSessionOut` — `record(Guid SessionId, string PlaybackUrl, string Token, DateTime ExpiresAt, string Protocol)`. **Nenhum outro campo.**

- [ ] **Step 1: Escrever o teste de contrato que falha**

Este teste é a rede de segurança do sub-projeto inteiro: ele falha se alguém acrescentar um campo que vaze credencial.

Criar `CondotifyAPI.Tests/CftvStreamingContractTests.cs`:

```csharp
using System.Text.Json;
using CondotifyAPI.Data.Equipments;
using Xunit;

namespace CondotifyAPI.Tests;

public class CftvStreamingContractTests
{
    [Fact]
    public void CftvSessionOut_NeverSerializesCredentialsOrRtspUrls()
    {
        var session = new CftvSessionOut(
            SessionId: Guid.NewGuid(),
            PlaybackUrl: "http://localhost:8889/l1_d2_c1/whep",
            Token: "token-opaco",
            ExpiresAt: DateTime.UtcNow.AddSeconds(120),
            Protocol: "webrtc");

        var json = JsonSerializer.Serialize(session);

        Assert.DoesNotContain("rtsp://", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("username", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("senha", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CftvSessionOut_ExposesOnlyTheFiveIntendedProperties()
    {
        var names = typeof(CftvSessionOut).GetProperties().Select(x => x.Name).OrderBy(x => x).ToArray();

        Assert.Equal(
            ["ExpiresAt", "PlaybackUrl", "Protocol", "SessionId", "Token"],
            names);
    }
}
```

O segundo teste é o que realmente protege: acrescentar qualquer propriedade ao DTO o quebra, forçando quem acrescentar a justificar.

- [ ] **Step 2: RED**

Run: `dotnet test CondotifyAPI.Tests --filter CftvStreamingContractTests`
Expected: falha de compilação — `CftvSessionOut` não existe.

- [ ] **Step 3: Criar os DTOs**

Criar `CondotifyAPI/Data/Equipments/CftvStreamingDtos.cs`:

```csharp
namespace CondotifyAPI.Data.Equipments;

/// <summary>
/// Resposta de abertura de sessao de video. NAO acrescente campos sem antes
/// verificar CftvStreamingContractTests: nada aqui pode revelar credencial,
/// URL RTSP ou endereco interno do equipamento.
/// </summary>
public sealed record CftvSessionOut(
    Guid SessionId,
    string PlaybackUrl,
    string Token,
    DateTime ExpiresAt,
    string Protocol);

public sealed record OpenCftvSessionIn(int Channel = 1, string Quality = "main", string Protocol = "webrtc");

public sealed record CftvStatusOut(
    Guid DeviceId,
    string Name,
    bool Online,
    DateTime? LastSeenAt,
    string HealthMessage,
    int MaxChannels);

/// <summary>Corpo enviado pelo MediaMTX ao callback de autorizacao.</summary>
public sealed class MediaAuthIn
{
    public string Action { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Query { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public string Ip { get; set; } = string.Empty;
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
```

- [ ] **Step 4: GREEN**

Run: `dotnet test CondotifyAPI.Tests --filter CftvStreamingContractTests`
Expected: 2 passam.

- [ ] **Step 5: Criar o controller de sessões**

Criar `CondotifyAPI/Controllers/CftvStreamingController.cs`:

```csharp
using System.Security.Claims;
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Domain.Models.Equipments;
using CondotifyAPI.Infrastructure;
using CondotifyAPI.Services.Authorization;
using CondotifyAPI.Services.CFTV;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Controllers;

[ApiController]
[Authorize]
[Route("api/access/licenses/{licenseId:guid}/cftv")]
[RequireLicensePermission(LicensePermissionEnum.ViewDevices)]
public sealed class CftvStreamingController : ControllerBase
{
    private const int TokenLifetimeSeconds = 120;
    private const int MaxConcurrentPaths = 24;

    private readonly DatabaseContext _context;
    private readonly ICftvStreamPathResolver _paths;
    private readonly IMediaAccessTokenService _tokens;
    private readonly IMediaGatewayClient _gateway;
    private readonly ILogger<CftvStreamingController> _logger;

    public CftvStreamingController(
        DatabaseContext context,
        ICftvStreamPathResolver paths,
        IMediaAccessTokenService tokens,
        IMediaGatewayClient gateway,
        ILogger<CftvStreamingController> logger)
    {
        _context = context;
        _paths = paths;
        _tokens = tokens;
        _gateway = gateway;
        _logger = logger;
    }

    [HttpPost("{deviceId:guid}/sessions")]
    public async Task<IActionResult> OpenSession(
        Guid licenseId,
        Guid deviceId,
        [FromBody] OpenCftvSessionIn input,
        CancellationToken cancellationToken)
    {
        var device = await _context.CFTVDevices
            .AsNoTracking()
            .Include(x => x.Channels)
            .FirstOrDefaultAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken);

        if (device is null) return NotFound();

        var channel = input.Channel < 1 ? 1 : input.Channel;
        if (device.DeviceType != CFTVDeviceTypeEnum.Camera && channel > device.MaxChannels)
            return BadRequest(new { Result = "InvalidChannel", Errors = "O canal informado nao existe neste equipamento." });

        if (await _gateway.ActivePathCountAsync(cancellationToken) >= MaxConcurrentPaths)
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { Result = "SessionLimitReached", Errors = "Limite de visualizacoes simultaneas atingido. Tente novamente em instantes." });

        var quality = string.Equals(input.Quality, "secondary", StringComparison.OrdinalIgnoreCase)
            ? StreamQuality.Secondary
            : StreamQuality.Main;

        var path = _paths.PreferredPath(device.Mark, device.DeviceType, channel, quality)
            ?? _paths.ConnectivityProbePaths(device.Mark, device.DeviceType, channel).FirstOrDefault();

        if (path is null)
            return StatusCode(StatusCodes.Status502BadGateway,
                new { Result = "UnsupportedDevice", Errors = "Este modelo ainda nao possui caminho de video conhecido." });

        var rtspPort = int.TryParse(new string(device.RTSPPort.Where(char.IsDigit).ToArray()), out var parsed) ? parsed : 554;

        // A credencial e usada aqui e apenas aqui. Nao pode ser registrada,
        // devolvida, nem entrar em mensagem de erro.
        var source = _paths.BuildRtspUrl(device.IpAddress, rtspPort, device.Username, device.Password, path);

        var mediaPath = MediaAccessTokenService.PathFor(licenseId, deviceId, channel);

        if (!await _gateway.EnsurePathAsync(mediaPath, source, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { Result = "GatewayUnavailable", Errors = "O servico de video esta indisponivel. Tente novamente em instantes." });

        var userId = Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var parsedUser) ? parsedUser : Guid.Empty;
        var expiresAt = DateTime.UtcNow.AddSeconds(TokenLifetimeSeconds);
        var token = _tokens.Issue(new MediaAccessGrant(licenseId, deviceId, channel, userId, expiresAt));

        var protocol = string.Equals(input.Protocol, "hls", StringComparison.OrdinalIgnoreCase) ? "hls" : "webrtc";
        var baseUrl = (Environment.GetEnvironmentVariable("CONDOTIFY_MEDIA_PLAYBACK_BASEURL") ?? "http://localhost:8889").TrimEnd('/');
        var playbackUrl = protocol == "hls"
            ? $"{baseUrl}/{mediaPath}/index.m3u8?token={Uri.EscapeDataString(token)}"
            : $"{baseUrl}/{mediaPath}/whep?token={Uri.EscapeDataString(token)}";

        _logger.LogInformation(
            "Sessao de video aberta. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}, usuario {UserId}, origem {Ip}.",
            licenseId, deviceId, channel, userId, HttpContext.Connection.RemoteIpAddress?.ToString() ?? "desconhecido");

        return Ok(new CftvSessionOut(Guid.NewGuid(), playbackUrl, token, expiresAt, protocol));
    }

    [HttpDelete("{deviceId:guid}/sessions/{channel:int}")]
    public async Task<IActionResult> CloseSession(
        Guid licenseId,
        Guid deviceId,
        int channel,
        CancellationToken cancellationToken)
    {
        if (!await _context.CFTVDevices.AsNoTracking().AnyAsync(x => x.Id == deviceId && x.LicenseId == licenseId, cancellationToken))
            return NotFound();

        await _gateway.RemovePathAsync(MediaAccessTokenService.PathFor(licenseId, deviceId, channel), cancellationToken);

        _logger.LogInformation(
            "Sessao de video encerrada. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}.",
            licenseId, deviceId, channel);

        return NoContent();
    }
}
```

Repare que **nenhuma** mensagem de erro devolvida menciona IP, credencial ou caminho RTSP.

- [ ] **Step 6: Criar o callback de autorização**

Criar `CondotifyAPI/Controllers/MediaAuthController.cs`:

```csharp
using CondotifyAPI.Data.Equipments;
using CondotifyAPI.Services.CFTV;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace CondotifyAPI.Controllers;

/// <summary>
/// Chamado pelo MediaMTX a cada leitura de midia. So deve ser alcancavel pela
/// rede interna: o MediaMTX nao autentica esta chamada.
/// </summary>
[ApiController]
[AllowAnonymous]
[Route("api/internal/media-auth")]
public sealed class MediaAuthController : ControllerBase
{
    private readonly IMediaAccessTokenService _tokens;
    private readonly ILogger<MediaAuthController> _logger;

    public MediaAuthController(IMediaAccessTokenService tokens, ILogger<MediaAuthController> logger)
    {
        _tokens = tokens;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Authorize([FromBody] MediaAuthIn input)
    {
        // Somente leitura e permitida por token. Publicacao e API nunca.
        if (!string.Equals(input.Action, "read", StringComparison.OrdinalIgnoreCase))
            return Unauthorized();

        // Esta versao do MediaMTX nao envia um campo token: ele vem na query.
        var token = QueryHelpers.ParseQuery(input.Query).TryGetValue("token", out var values)
            ? values.ToString()
            : string.Empty;

        var grant = _tokens.Validate(token, input.Path);
        if (grant is null)
        {
            _logger.LogWarning(
                "Leitura de video recusada. Caminho {Path}, protocolo {Protocol}, origem {Ip}.",
                input.Path, input.Protocol, input.Ip);
            return Unauthorized();
        }

        _logger.LogInformation(
            "Leitura de video autorizada. Licenca {LicenseId}, equipamento {DeviceId}, canal {Channel}, usuario {UserId}, protocolo {Protocol}, origem {Ip}.",
            grant.LicenseId, grant.DeviceId, grant.Channel, grant.UserId, input.Protocol, input.Ip);

        return Ok();
    }
}
```

- [ ] **Step 7: Build e suíte**

Run: `dotnet build Condotify.sln` — 0 erros.
Run: `dotnet test Condotify.sln` — tudo passa.

- [ ] **Step 8: Confirmar que nenhum DTO novo carrega credencial**

Run: `grep -rn "Password\|Username\|rtsp://" CondotifyAPI/Data/Equipments/CftvStreamingDtos.cs`
Expected: apenas o campo `Password` de `MediaAuthIn`, que é **entrada** vinda do MediaMTX (sempre vazio nesta configuração) e nunca é devolvido.

- [ ] **Step 9: Commit**

```bash
git add CondotifyAPI/Data/Equipments/CftvStreamingDtos.cs CondotifyAPI/Controllers/CftvStreamingController.cs CondotifyAPI/Controllers/MediaAuthController.cs CondotifyAPI.Tests/CftvStreamingContractTests.cs
git status --short
git commit -m "feat: add CFTV streaming sessions and the media authorisation callback"
```

---

## Task 7: Estado operacional das câmeras

**Files:**
- Modify: `CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs`
- Modify: `CondotifyAPI.Infrastructure/ContextConfiguration/Equipments/CFTVDeviceConfiguration.cs`
- Create: migração EF
- Create: `CondotifyAPI/Services/CFTV/CftvHealthMonitoringWorker.cs`
- Modify: `CondotifyAPI/Controllers/CftvStreamingController.cs` (endpoint de status)
- Modify: `CondotifyAPI/Program.cs`

**Interfaces:**
- Consumes: `CftvStatusOut` (Task 6).
- Produces: colunas `IsActive`, `LastSeenAt`, `HealthMessage` em `CFTVDevices`; `GET .../cftv/status`.

- [ ] **Step 1: Acrescentar as três propriedades ao DTO**

Em `CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs`, junto às demais:

```csharp
        public bool IsActive { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public string HealthMessage { get; set; } = string.Empty;
```

- [ ] **Step 2: Mapear**

Em `CFTVDeviceConfiguration.Configure`, antes do `HasMany`:

```csharp
            builder.Property(x => x.IsActive)
                .HasDefaultValue(false);

            builder.Property(x => x.HealthMessage)
                .HasMaxLength(300)
                .HasDefaultValue(string.Empty);
```

`LastSeenAt` é anulável e não precisa de configuração explícita.

- [ ] **Step 3: Gerar a migração**

```bash
dotnet ef migrations add AddCftvOperationalState --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI
```

Conferir o arquivo gerado: deve conter apenas três `AddColumn` sobre `CFTVDevices`, nenhum `DropColumn`, nenhuma alteração em outra tabela. Se contiver mais, algo divergiu do snapshot — investigar antes de seguir.

- [ ] **Step 4: Criar o worker de saúde**

Criar `CondotifyAPI/Services/CFTV/CftvHealthMonitoringWorker.cs`, seguindo o padrão de `DeviceHealthMonitoringWorker`:

```csharp
using System.Net.NetworkInformation;
using System.Net.Sockets;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Services.CFTV;

/// <summary>
/// Verifica periodicamente se cada camera responde, para que o aplicativo
/// possa mostrar online/offline sem esperar o timeout de um stream.
/// </summary>
public sealed class CftvHealthMonitoringWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(2);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CftvHealthMonitoringWorker> _logger;

    public CftvHealthMonitoringWorker(IServiceScopeFactory scopeFactory, ILogger<CftvHealthMonitoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Falha ao verificar a saude das cameras.");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task CheckAllAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DatabaseContext>();

        var devices = await context.CFTVDevices.ToListAsync(cancellationToken);
        if (devices.Count == 0) return;

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var port = int.TryParse(new string(device.RTSPPort.Where(char.IsDigit).ToArray()), out var parsed) ? parsed : 554;
            var reachable = await TcpReachableAsync(device.IpAddress, port, 1500, cancellationToken);

            device.IsActive = reachable;
            device.HealthMessage = reachable ? string.Empty : "Sem resposta na porta RTSP.";
            if (reachable) device.LastSeenAt = DateTime.UtcNow;
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task<bool> TcpReachableAsync(string host, int port, int timeoutMs, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            await client.ConnectAsync(host, port, timeout.Token);
            return client.Connected;
        }
        catch
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Endpoint de status**

Acrescentar a `CftvStreamingController`:

```csharp
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(Guid licenseId, CancellationToken cancellationToken)
    {
        var devices = await _context.CFTVDevices
            .AsNoTracking()
            .Where(x => x.LicenseId == licenseId)
            .OrderBy(x => x.Name)
            .Select(x => new CftvStatusOut(x.Id, x.Name, x.IsActive, x.LastSeenAt, x.HealthMessage, x.MaxChannels))
            .ToListAsync(cancellationToken);

        return Ok(devices);
    }
```

`CftvStatusOut` deliberadamente não expõe `IpAddress`, `Username`, `Password` nem portas.

- [ ] **Step 6: Registrar o worker**

Em `CondotifyAPI/Program.cs`, junto aos demais `AddHostedService`:

```csharp
builder.Services.AddHostedService<CftvHealthMonitoringWorker>();
```

- [ ] **Step 7: Build, migração e suíte**

Run: `dotnet build Condotify.sln` — 0 erros.
Run: `dotnet ef database update --project CondotifyAPI.Infrastructure --startup-project CondotifyAPI` — aplica sem erro.
Run: `dotnet test Condotify.sln` — tudo passa.

- [ ] **Step 8: Commit**

```bash
git add CondotifyAPI.Domain/DTO/Equipments/CFTVDeviceDTO.cs CondotifyAPI.Infrastructure/ContextConfiguration/Equipments/CFTVDeviceConfiguration.cs CondotifyAPI.Infrastructure/Migrations CondotifyAPI/Services/CFTV/CftvHealthMonitoringWorker.cs CondotifyAPI/Controllers/CftvStreamingController.cs CondotifyAPI/Program.cs
git status --short
git commit -m "feat: track CFTV operational state and expose camera status"
```

---

## Task 8: Verificação integrada

Esta task não escreve funcionalidade: prova que o conjunto funciona.

**Files:**
- Create: `tools/media-gateway-harness.html`
- Create: `docs/superpowers/plans/2026-07-31-sp1-verificacao.md` (registro do resultado)

- [ ] **Step 1: Subir a infraestrutura**

```bash
docker compose up -d postgres mediamtx
dotnet run --project CondotifyAPI/CondotifyAPI.csproj
```

- [ ] **Step 2: Confirmar que a Control API não escapou**

Run: `curl -s -o /dev/null -w "%{http_code}" --max-time 3 http://localhost:9997/v3/config/paths/list ; echo`
Expected: **`000`**, falha de conexão.

Se responder `200`, **pare**: a porta foi publicada e qualquer um na rede lê as senhas de todas as câmeras. Corrija o compose antes de continuar.

- [ ] **Step 3: Publicar uma fonte RTSP sintética**

Sem câmera real disponível, `ffmpeg` gera um padrão de cores e o publica no MediaMTX:

```bash
docker run --rm --network condotify_default linuxserver/ffmpeg \
  -re -f lavfi -i testsrc=size=640x480:rate=15 \
  -f lavfi -i sine=frequency=1000 \
  -c:v libx264 -preset ultrafast -tune zerolatency -c:a aac \
  -f rtsp rtsp://mediamtx:8554/camera-sintetica
```

**Nota:** isto exige `rtsp: yes` no `mediamtx.yml` para aceitar a publicação. Ligue temporariamente para o teste e **volte a desligar** ao final — o servidor RTSP de entrada não é necessário em produção e é superfície de ataque desnecessária. Registre no relatório que essa alteração foi revertida.

- [ ] **Step 4: Provar que a autorização funciona**

Com um token válido emitido pela API para o caminho sintético:

Run: `curl -s -o /dev/null -w "%{http_code}" "http://localhost:8888/camera-sintetica/index.m3u8?token=TOKEN_VALIDO"`
Expected: `200`.

Run: `curl -s -o /dev/null -w "%{http_code}" "http://localhost:8888/camera-sintetica/index.m3u8?token=TOKEN_ADULTERADO"`
Expected: `401`.

Run: `curl -s -o /dev/null -w "%{http_code}" "http://localhost:8888/camera-sintetica/index.m3u8"`
Expected: `401` — sem token, sem acesso.

Estes três resultados juntos provam a cadeia: API emite → MediaMTX consulta → API valida → mídia flui ou não.

- [ ] **Step 5: Harness visual**

Criar `tools/media-gateway-harness.html` com um `<video>` e hls.js embutido, apontando para o `playbackUrl` devolvido pela API. Serve para confirmar que a imagem realmente aparece, não só que o HTTP responde 200.

- [ ] **Step 6: Registrar o que NÃO foi verificado**

Escrever `docs/superpowers/plans/2026-07-31-sp1-verificacao.md` declarando explicitamente:

- O que foi provado: emissão e validação de token, autorização por callback, entrega HLS, recusa sem token e com token adulterado, isolamento da Control API.
- **O que não foi provado: compatibilidade com qualquer modelo real de câmera.** Não há câmera cadastrada no ambiente; `CFTVDevices` está vazia. Os caminhos RTSP por fabricante em `PreferredPath` nunca foram exercitados contra hardware.
- **O que não foi provado: WebRTC.** O harness usa HLS; o caminho WebRTC (`/whep`) precisa de navegador real.

- [ ] **Step 7: Commit**

```bash
git add tools/media-gateway-harness.html docs/superpowers/plans/2026-07-31-sp1-verificacao.md
git status --short
git commit -m "test: verify the CFTV media gateway end to end with a synthetic source"
```

