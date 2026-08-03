# Data, hora e clima no Início

Data: 2026-08-03

## Objetivo

Adicionar uma faixa discreta no Início, entre a saudação e os cartões de métrica, mostrando data, hora e o clima atual (via localização do aparelho). Decidido com o usuário: API de clima **Open-Meteo** (gratuita, sem chave), faixa **abaixo da saudação**.

Escopo: só `Condotify.Mobile`. Não altera `CondotifyAPI` nem `Condotify.ApiClient` — clima é uma chamada direta do app pra Open-Meteo, sem passar pelo backend.

## Comportamento

- Ao abrir/atualizar o Início, o app pede a localização do aparelho (permissão `ACCESS_COARSE_LOCATION` — suficiente para clima por cidade, mais discreta que localização precisa) e busca o clima atual via Open-Meteo (`GET https://api.open-meteo.com/v1/forecast?latitude=...&longitude=...&current_weather=true`, sem chave de API).
- A busca de clima roda em paralelo com o carregamento do dashboard, sem bloquear nada — a data/hora aparece imediatamente, o clima "encaixa" quando (e se) a resposta chegar.
- **Falha graciosa, sempre**: permissão negada, sem GPS, sem internet, timeout, erro da API — em qualquer um desses casos a faixa mostra só a data/hora, sem ícone de clima, sem mensagem de erro, sem popup de exceção. Clima é um extra, nunca pode quebrar a tela.
- Data/hora no formato numérico já usado no resto do app (`dd/MM · HH:mm`), sem depender de nomes de mês/dia por extenso (o app evita `CultureInfo` em outros lugares por segurança de build/trim; mantemos o padrão).
- O botão "Atualizar" existente no Início também atualiza data/hora e tenta buscar o clima de novo.

## Detalhes técnicos

- Localização: `Microsoft.Maui.Devices.Sensors.Geolocation` (`GetLastKnownLocationAsync`, com `GetLocationAsync` como fallback se não houver uma localização em cache — timeout curto, ~6s, pra não travar a tela).
- Permissão: `Permissions.RequestAsync<Permissions.LocationWhenInUse>()` antes de chamar a geolocalização; `AndroidManifest.xml` ganha `ACCESS_COARSE_LOCATION`.
- Novo serviço `Condotify.Mobile/Services/MobileWeatherService.cs`, registrado como singleton, seguindo o mesmo padrão de `IHttpClientFactory` + cliente nomeado já usado por `MobileSessionCoordinator` (cliente novo `"OpenMeteo"` com `BaseAddress` fixo, registrado em `MauiProgram.cs`).
- Mapeamento do código de clima (WMO, retornado pela Open-Meteo) para um ícone Material + descrição curta em português — só os códigos mais comuns (limpo, nublado, chuva, neve, trovoada); qualquer código fora da tabela cai num ícone neutro genérico em vez de travar.

## Testes

Sem infraestrutura de teste de UI. A lógica de mapeamento de código de clima → (ícone, descrição) é pura e testável — ganha testes unitários em `Condotify.Mobile.Tests` (o único teste possível aqui sem depender de GPS/rede reais). Verificação do restante: build + instalação real no dispositivo Android conectado, com e sem permissão de localização concedida (pra confirmar o fallback gracioso).

## Fora de escopo

- Previsão estendida (só o clima atual).
- Mostrar clima em outras páginas além do Início.
- iOS/Windows: a permissão/chamada usa a API cross-platform do MAUI, então deveria funcionar nessas plataformas também, mas só o Android é testado nesta rodada (único dispositivo físico disponível).
