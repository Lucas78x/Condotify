# Validação da revisão de 07/09/2026

Base: `3fff2e6`. Ambiente local: Windows, SDK .NET 10.0.400. A primeira seção registra a base antes das mudanças; os resultados da implementação aprovada estão na seção posterior.

## Testes executados

| Projeto | Alvo | Aprovados | Falhas | Ignorados |
| --- | --- | ---: | ---: | ---: |
| Condotify.Web.Tests | net8.0 | 30 | 0 | 0 |
| Condotify.ApiClient.Tests | net8.0 | 46 | 0 | 0 |
| Condotify.Mobile.Tests | net10.0 | 94 | 0 | 0 |
| **Total** | | **170** | **0** | **0** |

O teste do portal também compilou `Condotify`, `Condotify.Contracts`, `Condotify.ApiClient` e `Condotify.UI`. Os testes mobile compilaram o núcleo compartilhado; não geraram APK, IPA nem validaram o aplicativo MAUI em dispositivo.

As primeiras tentativas de restauração não concluíram pelo caminho padrão. A restauração foi concluída usando exclusivamente o cache NuGet já instalado, sem auditoria remota, e um único nó de MSBuild. Não houve alteração das dependências. Essa execução não representa auditoria de vulnerabilidades ou validação de instalação em uma máquina limpa.

Comandos usados, substituindo `<projeto>` pelos três projetos da tabela:

```powershell
$env:DOTNET_CLI_HOME = 'D:\repos\Condotify\.codex_runtime'
$env:NUGET_PACKAGES = 'C:\Users\Lucas Noc\.nuget\packages'
dotnet restore '<projeto>\<projeto>.csproj' --source 'C:\Users\Lucas Noc\.nuget\packages' -p:NuGetAudit=false -m:1 -nodeReuse:false
dotnet test '<projeto>\<projeto>.csproj' --no-restore -m:1 -nodeReuse:false --logger 'trx' --results-directory artifacts\project-review
```

Logs e TRX da execução ficam em `D:\repos\Condotify\artifacts\project-review`, diretório local ignorado pelo Git.

## Protótipo da proposta

- Renderizado e inspecionado no navegador em temas claro e escuro.
- Conferido em larguras nominais de 1.024, 736 e 360 px. No renderizador independente, a barra de rolagem reduziu algumas áreas úteis para 721 e 345 px; não foi detectado transbordamento horizontal nas três telas.
- Verificadas a seleção de pendência e a mudança do detalhe, atribuição local de atendimento, troca entre portaria/credenciais/morador, solicitação de reenvio e detalhe de encomenda.
- O reenvio mantém estado pendente e informa solicitação na fila; não simula confirmação de equipamento sem resposta.
- Dados são fictícios. As interações não consultam API, enviam mensagens ou acionam equipamentos.

## Limites

- Não executada a suíte completa `CondotifyAPI.Tests`: parte dela escreve em PostgreSQL e usa por padrão um banco chamado `Condotify`. Não foi provisionado um banco isolado nesta revisão.
- Não realizados testes de controladores físicos, mídia ao vivo, produção, CI remoto ou build completo de workloads MAUI.
- Os 170 testes aprovados verificam a base existente. Eles não validam correções futuras nem contradizem os problemas identificados por inspeção do código.
- As propostas de usabilidade precisam ser validadas com operadores, gestores e moradores. Não há medição de tempo economizado, conversão ou conformidade integral de acessibilidade.


## Implementação aprovada — validação posterior

| Suíte | Aprovados | Falhas | Escopo |
| --- | ---: | ---: | --- |
| Condotify.Web.Tests | 33 | 0 | Portal, contexto e renderização do resumo compartilhado |
| Condotify.ApiClient.Tests | 50 | 0 | Contratos HTTP, incluindo resumo, paginação e distribuição posterior |
| Condotify.Mobile.Tests | 94 | 0 | Núcleo mobile, sem empacotamento MAUI |
| CondotifyAPI.Tests (filtro explícito) | 34 | 0 | Indicadores, datas, portaria, validação do cadastro e escopo do resumo |
| **Total** | **211** | **0** | Suítes e filtros acima |

O filtro da API inclui `OperationalIndicatorTests`, `CentralCredentialValidationTests`, `CondotifyTimeTests`, `ConciergeDashboardTests` e `ResidentHomeSummaryTests`. O teste de SQL usa `ToQueryString()` com Npgsql, sem conectar a um banco; validou a tradução de `AT TIME ZONE` e das expressões de escopo. Os testes do cadastro central validam entrada e transporte, não uma gravação real em PostgreSQL.

Logs posteriores: `api-final-test.log`, `web-final-test.log`, `client-implementation-test.log` e `mobile-final-test.log`. A compilação do host de revisão também concluiu sem avisos e sem erros.

### Componentes implementados no navegador

- Portaria, credenciais, resumo do morador e visão de gestão em 1.280, 736 e 360 px, nos temas claro e escuro (24 combinações). Sem transbordamento horizontal do documento. As barras de rolagem reduziram algumas larguras úteis em 10 px.
- Confirmadas seleção de pendência, troca do detalhe, bloqueio da aprovação com dados antigos, seleção de credencial sem vínculo e abertura do formulário existente.
- Console sem erro de execução de componente; houve apenas um 404 do favicon no host local de revisão.
- Capturas `implemented-*.png`, host `ui-harness` e estilos exportados em `artifacts/project-review`.
- Alguns checks iniciais leram o DOM antes da atualização do Blazor; foram corrigidos para esperar o estado esperado. A aprovação bloqueada e a troca de detalhe foram confirmadas após essa espera.

### Limites da homologação

A suíte completa da API, gravação transacional do cadastro central, mudança de condomínio em sessão autenticada, reconexão SignalR com servidor real, empacotamento MAUI e comunicação com terminais ainda precisam de homologação no ambiente apropriado. A implementação não foi publicada. O diagnóstico inicial e seus testes de base permanecem acima para rastreabilidade.
