# Reforma da UX da Portaria — Design

**Status:** aprovado pelo usuário, pronto para virar plano de implementação.

## Contexto

A tela `/portaria` (`Condotify/Components/Pages/Concierge.razor`) é o cockpit ao vivo do porteiro: agenda de visitas, KPIs, acionamento remoto de portas, eventos recentes e watchlist. Um levantamento do código atual (ver `docs/superpowers/specs/` — não há um doc de levantamento separado, o resultado está resumido aqui) achou três problemas concretos, todos escolhidos pelo usuário para esta reforma:

1. **Navegação fragmentada**: Encomendas (`DeliveriesModule.razor`) e Eventos de acesso (`AccessEventsModule.razor`) vivem em `/licencas/{id}/{section}` — um workspace tabulado separado, orientado a configuração/admin — forçando o porteiro a sair do cockpit ao vivo para tarefas do dia a dia.
2. **Sem tempo real**: a única atualização automática é um `PeriodicTimer` de 15s que recarrega o dashboard inteiro. Não existe SignalR (nem qualquer push) em nenhum lugar da solução hoje.
3. **Placa em texto livre + Encomendas sem busca/foto**: a criação de visita tem um campo de placa em texto livre, sem lookup contra veículos já cadastrados; o módulo de Encomendas não tem busca/filtro, e os campos `PhotoUrl`/`DeliveryProofUrl` de `DeliveryDTO` existem no modelo mas nunca são preenchidos por nenhuma tela.

**Fora de escopo** (avaliado e descartado nas perguntas de esclarecimento):
- Check-in por QR/câmera no desktop (o endpoint `POST .../concierge/visits/scan` já existe e funciona no mobile; ativá-lo no desktop fica para depois).
- Integração com LPR/OCR (câmera lê placa automaticamente) — a busca de placa nesta reforma é só autocomplete contra veículos já cadastrados, sem hardware/câmera envolvidos.
- Status online/offline de terminal em tempo real — continua no heurístico de 5 minutos existente, sem push.
- Rotas de acesso (`AccessRoutesModule`) continua fora da Portaria — é configuração de política, editada por síndico/admin, não operação de turno.

## Arquitetura

Três frentes largamente independentes, todas dentro do mesmo plano/spec porque compartilham a mesma tela-alvo (`/portaria`) e devem ser sequenciadas com cuidado (a consolidação de navegação muda a estrutura de abas que as outras duas frentes vão popular):

1. **Consolidação de navegação**: `Concierge.razor` ganha abas (`MudTabs`) — Agenda de acessos (conteúdo atual), Encomendas, Eventos. `DeliveriesModule.razor` e `AccessEventsModule.razor` são portados para dentro de `Concierge.razor` como painéis de aba (não duas implementações paralelas — o conteúdo migra, as rotas antigas dentro do workspace de licença deixam de existir). `AccessRoutesModule` não muda.
2. **Tempo real via SignalR**: hub novo `ConciergeHub` (`CondotifyAPI/Hubs/ConciergeHub.cs`), grupo por `licenseId`. Três mensagens: `AccessEventRecorded`, `VisitStatusChanged`, `DeliveryUpdated`. Publicadas nos pontos que já gravam essas mudanças hoje (`ConciergeController`, `AccessEventIngestionWorker`, o novo endpoint/ação de encomendas). O portal assina ao entrar em `/portaria`, desassina ao sair. O polling de 15s vira fallback de 60s.
3. **Placa + Encomendas**: endpoint novo `GET api/access/licenses/{licenseId}/vehicles/search?plate=` + autocomplete no `ConciergeVisitDialog`. Busca/filtro no painel de Encomendas (agora dentro da Portaria). Captura de foto (chegada e entrega) reaproveitando o padrão `InputFile` → base64 já usado para foto de rosto em `ConciergeVisitDialog`.

## Componentes

### 1. Navegação

- **`Concierge.razor`** (modificado): ganha um `MudTabs` envolvendo o conteúdo atual (Agenda de acessos) mais duas novas abas. KPI strip e painel de Acionamentos ficam fora das abas, sempre visíveis (ações rápidas úteis em qualquer contexto).
- **Conteúdo de Encomendas**: o `@code` e o markup de `DeliveriesModule.razor` são movidos para um novo componente `Condotify/Components/Concierge/ConciergePackagesTab.razor`, referenciado de dentro de `Concierge.razor`. `DeliveriesModule.razor` é apagado; a rota `/licencas/{id}/encomendas` deixa de existir (ou redireciona para `/portaria?aba=encomendas`, a decidir na etapa de plano conforme o que for mais simples).
- **Conteúdo de Eventos**: hoje `AccessEventsModule` mostra eventos de **um dispositivo por vez** (seleção manual). A versão portada (`Condotify/Components/Concierge/ConciergeEventsTab.razor`) precisa de um endpoint que combine eventos de todos os dispositivos da licença — hoje não existe; `ConciergeController.Dashboard` já traz os últimos 80 eventos combinados, então a nova aba consome uma versão paginada/filtrável dessa mesma consulta ao invés do endpoint por-dispositivo. `AccessEventsModule.razor` é apagado; `/licencas/{id}/acessos` deixa de existir.
- **`LicenseWorkspace.razor`**: remove as entradas de menu/rota para Encomendas e Eventos (que deixam de existir como abas separadas). Rotas continua como está.

### 2. Tempo real

- **`ConciergeHub.cs`** (novo, `CondotifyAPI/Hubs/`): `Hub` do SignalR. Método `JoinLicenseGroup(Guid licenseId)` chamado pelo cliente ao conectar — valida que o principal autenticado tem acesso àquela licença (mesma checagem de `ILicenseAuthorizationService` usada nos controllers) antes de adicionar ao grupo `license-{licenseId}`. Sem esse check, um porteiro autenticado poderia se inscrever no grupo de outra licença e vazar eventos de acesso de outro condomínio.
- **Publicação de eventos**: `IHubContext<ConciergeHub>` injetado em `ConciergeController` (mudança de status de visita → `VisitStatusChanged`), `AccessEventIngestionWorker` (novo evento de acesso → `AccessEventRecorded`), e no endpoint de encomendas dentro do próprio `ConciergeController` (registro de chegada/entrega → `DeliveryUpdated`). Cada mensagem carrega o `licenseId` e o payload já serializado (mesmo DTO que o REST retornaria), para o cliente não precisar de uma chamada extra.
- **Cliente (`Concierge.razor`)**: usa `Microsoft.AspNetCore.SignalR.Client` (pacote novo no `Condotify.csproj`), conecta ao entrar na página, assina os três tipos de mensagem, atualiza o estado local em memória (sem recarregar a página inteira) e desconecta em `DisposeAsync`. O `PeriodicTimer` existente passa de 15s para 60s e vira só uma rede de segurança caso a conexão caia (reconexão automática do `HubConnectionBuilder` cobre a maior parte dos casos).
- **Reconexão/erro**: se a conexão SignalR cair, mostrar um indicador discreto (ex.: um chip "Reconectando…") e confiar no fallback de 60s até reconectar — sem bloquear a tela nem exigir ação do usuário.

### 3. Placa + Encomendas

- **`GET api/access/licenses/{licenseId}/vehicles/search?plate=`** (novo, em `PeopleManagementController` ou `ConciergeController` — a decidir no plano conforme onde os outros endpoints de veículo já vivem): busca por prefixo/contains na placa, entre todas as unidades da licença, retornando `{ VehicleId, Plate, ResidentName, UnitLabel }`, limitado a ~10 resultados (mesmo padrão de `SearchResidentsAsync`).
- **`ConciergeVisitDialog.razor`**: campo de placa vira `MudAutocomplete` seguindo o padrão do autocomplete de morador já existente no mesmo arquivo (mínimo 2 caracteres, debounce). Ao selecionar, preenche a placa e mostra "de quem é" (nome + unidade) como texto auxiliar. Segue aceitando texto livre para visitantes sem veículo cadastrado (não é campo obrigatoriamente vinculado a um veículo).
- **`ConciergePackagesTab.razor`** (o `DeliveriesModule` portado): ganha uma caixa de busca (texto livre sobre morador/unidade/código de rastreio, filtragem client-side sobre a página carregada — mesmo padrão já usado na Agenda de acessos) e um filtro por status (`MudSelect`), mesmo padrão do `AccessEventsModule` atual.
- **Captura de foto**: `DeliveryFormDialog.razor` ganha um campo de foto (`InputFile` → `FacePhotoProcessor`-style helper → base64) ao registrar a chegada, preenchendo `DeliveryDTO.PhotoUrl`. Um novo diálogo pequeno (ou uma extensão do fluxo de "marcar como entregue" existente) captura uma segunda foto ao confirmar a entrega ao morador, preenchendo `DeliveryProofUrl`. O helper de processamento de imagem (`FacePhotoProcessor` ou equivalente) é reaproveitado — não reinventado — já que o padrão (redimensionar, comprimir, converter para base64 no cliente) já existe e funciona para o fluxo de foto de rosto.

## Fluxo de dados (tempo real)

```
Evento de acesso chega no terminal
  → AccessEventIngestionWorker grava no banco
  → IHubContext<ConciergeHub>.Clients.Group("license-{id}").SendAsync("AccessEventRecorded", payload)
  → Concierge.razor (assinado no grupo) recebe, insere na lista "Eventos recentes"/aba Eventos, sem reload

Porteiro faz check-in de uma visita
  → ConciergeController.UpdateStatus grava no banco
  → mesmo IHubContext envia "VisitStatusChanged"
  → toda aba de Portaria aberta (mesma licença, outro porteiro em outra estação) atualiza a linha e os KPIs
```

## Tratamento de erros

- **`ConciergeHub.JoinLicenseGroup`**: se o principal não tiver acesso à licença pedida, rejeitar a inscrição (não adicionar ao grupo, não lançar exceção que derrube a conexão) — mesma postura fail-closed do resto do sistema de tenant isolation.
- **Conexão SignalR indisponível/caindo**: reconexão automática do client SDK; enquanto não reconectar, o fallback de polling de 60s garante que a tela nunca fica “presa” sem novos dados por mais de 1 minuto.
- **Busca de placa sem resultado**: autocomplete mostra "Nenhum veículo encontrado — pode digitar a placa manualmente", sem bloquear o formulário (mesma UX de graceful-degradation do autocomplete de morador).
- **Falha ao processar foto de encomenda**: mesma UX já usada em `ConciergeVisitDialog.ReadPhotoAsync` — mensagem de erro inline, sem bloquear o resto do formulário (a foto é opcional; entrega/chegada podem ser registradas sem foto se o processamento falhar).

## Testes

- **Backend**: testes de integração para o novo endpoint de busca de placa (retorna resultados corretos, respeita isolamento de tenant — não deve vazar veículos de outra licença); testes para `ConciergeHub.JoinLicenseGroup` (aceita quando autorizado, rejeita quando não); testes para os pontos de publicação (o evento certo é enviado quando o status muda / quando um evento de acesso é ingerido / quando uma encomenda é registrada ou entregue).
- **Frontend**: os componentes Blazor existentes neste projeto não têm suíte de testes de UI automatizada (bUnit ou similar não está configurado) — consistente com o resto do portal, a verificação desta reforma na ponta do cliente será manual (rodar o portal localmente, testar os fluxos), não uma nova suíte de testes de componente introduzida só para esta feature.
- **Não-regressão**: a suíte de testes de backend existente (incluindo os testes de isolamento de tenant já em vigor) deve continuar passando integralmente — os endpoints portados/novos não devem escapar do filtro global de licença já implementado.

## Decisões YAGNI (explicitamente fora de escopo)

- Sem QR/câmera no desktop, sem LPR/OCR, sem push de status de terminal — ver seção "Fora de escopo" acima.
- Sem bUnit/testes de componente Blazor novos — não é o padrão hoje neste projeto.
- Sem redesenho visual dos componentes existentes (KPIs, cards, cores) além do necessário para caber as novas abas — isto é uma reforma de fluxo/funcionalidade, não um redesign visual.
