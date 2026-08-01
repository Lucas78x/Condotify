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

## Tasks 4 a 8

As tasks seguintes dependem de decisões que só podem ser tomadas com o MediaMTX rodando localmente e com a resposta real da sua Control API, cujo formato varia entre versões maiores. Serão detalhadas com o mesmo nível das anteriores assim que a Task 3 estiver aprovada e a imagem estiver fixada em uma versão concreta.

Escopo previsto:

- **Task 4 — MediaMTX no compose.** Serviço com imagem em versão fixa, `mediamtx/mediamtx.yml` com `authMethod: http` apontando para o callback, `9997` sem publicação de porta, variáveis novas em `.env.example`.
- **Task 5 — `MediaGatewayClient`.** Cliente tipado da Control API: garantir caminho, remover caminho, consultar caminhos ativos. `HttpClient` nomeado, como já se faz em `AddHttpClient("AlertNotifications", ...)`.
- **Task 6 — Endpoints de sessão e callback.** `CftvStreamingController` com `POST`/`DELETE` de sessões e `MediaAuthController` com o callback. Auditoria de cada abertura. Códigos de erro distintos conforme a tabela do spec.
- **Task 7 — Snapshot, saúde e migração.** Três colunas novas em `CFTVDevices`, migração, `CftvHealthMonitoringWorker` reaproveitando `PingAsync`/`TcpPortOpenAsync`, endpoint de snapshot e de status.
- **Task 8 — Verificação integrada.** Fonte RTSP sintética publicada por `ffmpeg` no MediaMTX, prova do caminho RTSP→HLS→autorização fim a fim, e harness HTML com player. Registrar explicitamente que a compatibilidade por modelo de fabricante **não** foi verificada.

O `CftvStreamingContractTests`, que garante que nenhum DTO de saída serializa `Password`, `UserName` ou `rtsp://`, entra na Task 6 junto dos DTOs.
