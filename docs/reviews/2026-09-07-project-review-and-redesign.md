# Condotify / F&F Access — diagnóstico e proposta de redesign

Data: 07/09/2026. Base analisada: `3fff2e6` (`fix/expired-invite-presentation`). Branch de trabalho: `design/project-review-and-redesign`.

## Recomendação

Evoluir o produto para uma operação orientada a decisões: mostrar o que exige atenção, explicar o contexto e oferecer a próxima ação no mesmo lugar. Preservar a identidade F&F Access, o portal Blazor/MudBlazor, o aplicativo MAUI e as integrações existentes.

Começar pela confiabilidade dos indicadores e pela central de portaria. Depois, simplificar o contexto de condomínio, o ciclo de credenciais e a rotina do morador. A prioridade considera impacto operacional e dependências técnicas; não é uma medição de uso ou retorno financeiro.

Este documento preserva o diagnóstico da base analisada. Após aprovação do usuário, as mudanças foram implementadas na cópia de trabalho; consulte [implementation.md](implementation.md) para o estado atual. O protótipo original usa dados fictícios e interações locais.

## Escopo e base existente

O repositório contém 13 projetos .NET, 137 componentes Razor e 123 arquivos C# em projetos de testes, conforme `git ls-files` na base analisada. Isso não representa quantidade de testes executados ou cobertura.

| Camada | Responsabilidade observada |
| --- | --- |
| `Condotify` | Portal Blazor Server/MudBlazor, autenticação web por cookie e componentes operacionais |
| `Condotify.Mobile` / `Condotify.Mobile.Core` | Aplicativo MAUI Blazor Hybrid e serviços de sessão, conectividade e operação móvel |
| `Condotify.Contracts` / `Condotify.ApiClient` / `Condotify.UI` | Contratos, comunicação HTTP e tema compartilhados |
| `CondotifyAPI` | API ASP.NET Core, autorização, SignalR, serviços de domínio e workers |
| `CondotifyAPI.Domain` / `CondotifyAPI.Infrastructure` | Modelos, contratos de domínio, EF Core e persistência PostgreSQL |
| `lpr-ocr`, `mediamtx`, `deploy` | Reconhecimento de placas, mídia e infraestrutura de execução |

Há funcionalidades relevantes já implementadas: permissões por condomínio, filtro de tenant no EF, políticas distintas para equipe e morador, auditoria, reconciliação de credenciais, alertas persistentes, operação offline protegida, boletos, financeiro, documentos, assembleias e manutenção. As propostas abaixo evoluem essa base; não tratam essas funções como ausentes.

A revisão combina leitura de código, estilos, configuração de CI, testes e propostas anteriores em `docs/superpowers`. Não houve observação de usuários, benchmark de banco, teste com controladores físicos ou inspeção da produção. As observações visuais da interface atual derivam dos componentes e estilos, não de uma sessão autenticada do produto.

## 1. Corrigir a confiança nos indicadores — prioridade P0

**Achados verificáveis no código:**

| Evidência | Consequência | Mudança proposta |
| --- | --- | --- |
| `Condotify/Components/Pages/Dashboard.razor`: o erro de monitoramento exibe aviso, mas os valores usam `?? 0`, a sincronização usa `?? "100,0"` e o bloco sem alertas mostra “Operação saudável” | Uma falha de consulta pode produzir indicadores visualmente positivos e contraditórios | Representar `carregando`, `disponível`, `desatualizado`, `indisponível` e `sem permissão` separadamente; usar “—” para valor desconhecido |
| `CondotifyAPI/Controllers/OperationsController.cs`, `GetDashboard`: `OnlineDeviceCount` considera `IsActive`; `ConciergeController.Dashboard` exige também `LastSeenAt` nos últimos cinco minutos | As telas podem discordar sobre o mesmo equipamento quando a informação envelhece; o worker atualiza `IsActive`, mas a visão geral não aplica validade temporal | Centralizar a definição de saúde e informar última comunicação e validade da leitura |
| `ConciergeController.Dashboard`: carrega os últimos 80 eventos e calcula `DeniedToday` sobre essa lista | Em dias com mais de 80 eventos, negações anteriores podem sumir do total diário | Contar no banco no intervalo completo; manter o limite de 80 somente para o feed |
| `ConciergeController.Dashboard` e `OperationsController.GetDashboard` usam a virada do dia em UTC | “Hoje” pode divergir do dia visto pelo operador; a Bahia está três horas atrás de UTC | Definir o fuso operacional do condomínio e converter o intervalo local para UTC na consulta |
| `Concierge.razor`: `Closed` define `_reconnecting = false`; uma falha inicial do hub é capturada sem mudar o estado visual | “Operação sincronizada” pode aparecer quando há apenas atualização periódica por HTTP | Exibir estados distintos: tempo real conectado, atualização periódica, reconectando e dados desatualizados |

**Critérios de aceite:** uma falha de telemetria nunca apresenta saúde positiva; o contador diário permanece exato com mais de 80 eventos; a virada do dia acompanha o fuso configurado; portaria e visão geral concordam sobre equipamentos sem comunicação recente; a queda do hub informa o modo de atualização efetivamente disponível.

Antes de redesenhar os números, acrescentar testes de regressão desses cenários. A existência de classes de testes operacionais no repositório não comprova que esses casos estejam cobertos.

## 2. Portaria como estação de trabalho — prioridade P1

**Hoje:** `Concierge.razor` apresenta seis indicadores, uma área de atenção, abas e uma lista de visitas com várias ações no menu de três pontos. Os detalhes de evento e visita abrem diálogos. A página também contém conexão SignalR, recarga periódica e interpretação de formatos de portas dos fabricantes.

**Redesign proposto:**

- Cabeçalho compacto com condomínio, posto, período e estado real da conexão.
- Fila principal com agrupamento “Exige decisão”, “Em atendimento” e “Concluído”. Ordenação por gravidade e tempo de espera, preservando a ordem enquanto o operador atua.
- Selecionar uma pendência abre um painel lateral com pessoa, unidade, motivo, credencial, equipamento e histórico. Em telas estreitas, o detalhe fica abaixo da lista.
- Mostrar a ação contextual com texto: “Revisar autorização”, “Registrar saída” ou “Verificar equipamento”. Ações secundárias permanecem no menu.
- Distinguir registrar entrada, aprovar visita e acionar uma porta. No acionamento físico, manter a confirmação já existente com condomínio, terminal, porta e motivo visíveis.
- Permitir assumir um atendimento e identificar o responsável. Esse recurso exige persistência e controle de concorrência; não existe apenas por mudar o layout.

**Preservar:** `ConciergeEventsTab`, `ConciergePackagesTab`, permissões, confirmação de acionamento, auditoria e fallback de consulta. Reutilizar os contratos de eventos existentes.

**Aceite:** abrir uma pendência com uma seleção e encontrar a próxima ação sem procurar em outro módulo; recuperar o filtro ao fechar detalhes; manter o item selecionado durante atualizações; bloquear ações duplicadas em andamento; informar o resultado e devolver o foco ao ponto de origem.

## 3. Um contexto de condomínio, navegação por trabalho — prioridade P1

**Hoje:** `NavMenu.razor` oferece navegação global; `LicenseWorkspace.razor` adiciona cabeçalho, navegação de áreas e subnavegação. `LicenseLauncher.razor` resolve acessos globais a módulos mediante escolha de condomínio. O cabeçalho principal continua “Central de operações” em diferentes páginas.

**Proposta:** manter “Todos os condomínios” para administradores com visão de portfólio e oferecer seleção persistente de um condomínio no cabeçalho. Com um condomínio selecionado, apresentar grupos estáveis: Operação, Pessoas e acessos, Gestão e Administração. Dentro deles, manter módulos existentes e URLs profundas.

O contexto deve estar expresso na URL e ser validado pela API; uma preferência no navegador só ajuda a restaurar a última escolha autorizada. Ao trocar de condomínio, cancelar consultas anteriores, limpar seleção e dados antigos e restaurar apenas filtros compatíveis. Não esconder módulos permitidos com base apenas no nome do cargo: derivar ações e navegação das permissões e módulos habilitados já existentes.

**Aceite:** um link abre diretamente o condomínio e módulo corretos; voltar preserva filtros; a troca de contexto não deixa dados do condomínio anterior na tela; equipe com um único condomínio não passa por seleção repetitiva.

## 4. Credenciais como ciclo completo de acesso — prioridade P1

**Hoje:** `CredentialsModule.razor` já distingue cadastro e vínculos por terminal, mas mostra cada vínculo na linha da lista. “Nova credencial” depende de algum equipamento ativo. `CredentialFormDialog.razor` exige equipamento e apresenta “Salvar e sincronizar”.

**Proposta:** lista focada em pessoa, unidade, tipo, validade e resumo de distribuição (“2 de 3 terminais”). Ao selecionar, abrir a matriz por terminal com estado, última tentativa, motivo e ação de recuperação. Mostrar uma linha do tempo: cadastro → envio → confirmação → suspensão/expiração → remoção confirmada.

Permitir cadastro central e distribuição posterior para tipos compatíveis, mediante mudança explícita do contrato da API. O cadastro sem equipamento não deve comunicar acesso liberado. A captura facial que depende de terminal permanece um fluxo próprio. A foto não deve passar a ser persistida apenas para acomodar o redesign.

**Aceite:** suspensão central e remoção física pendente aparecem como estados distintos; falha em um terminal não oculta sucesso nos demais; reenvio respeita as chaves de idempotência e reconciliação existentes; credencial cadastrada sem distribuição não aparece como utilizável na porta.

## 5. Início do morador orientado ao próximo compromisso — prioridade P2

**Hoje:** `Condotify.Mobile/Components/Pages/Home.razor` já diferencia morador e equipe e prioriza “Autorizar visitante”. Depois aparecem notificações, um cartão de reservas cujo número representa unidades e a lista de vínculos residenciais. O clima também ocupa o início da tela.

**Proposta:** manter “Autorizar visitante” e organizar “Seu dia” com conteúdo diretamente acionável: encomenda aguardando retirada, próxima reserva, convite em andamento e cobrança disponível. Cada item mostra situação, data e destino claro. O acesso à carteirinha/passe digital fica próximo da identificação do condomínio. As unidades e o clima tornam-se informação secundária.

O resumo exige um endpoint do morador que respeite vínculo, módulos e permissões; evitar uma requisição para cada cartão. Não reutilizar o dashboard administrativo. Mostrar data do último resumo salvo e preservar as restrições da operação offline existentes.

**Aceite:** os cartões levam ao item indicado; datas e quantidades representam o conteúdo descrito; módulos desabilitados somem; ausência de informação é distinguida de ausência de pendência; estados expirada, cancelada e aguardando aprovação mantêm linguagem consistente com o portal.

## 6. Visão do gestor e implantação guiada — prioridade P2

**Hoje:** `OverviewModule.razor` destaca estrutura, quantidade de unidades e validade da licença; o onboarding aparece apenas quando não há blocos e mostra progresso fixo de 25%. O financeiro já possui busca e paginação de 50 itens em `FinancialManagementModule.razor`.

**Proposta:** quando o condomínio já opera, priorizar decisões de gestão: cobranças a revisar, reservas a aprovar, ordens de serviço atrasadas e comunicados a acompanhar. Cada resumo leva ao módulo com o filtro correspondente, sem duplicar o processamento financeiro existente. Mover validade comercial para Administração, mantendo aviso quando exigir ação.

Transformar a implantação em checklist calculado a partir da configuração real: estrutura, equipe, equipamentos testados, primeira credencial validada e módulos necessários. Etapas opcionais não bloqueiam módulos independentes. Mostrar o motivo de cada bloqueio e a ação para removê-lo.

**Aceite:** criar um bloco não encerra prematuramente a orientação; o progresso corresponde a etapas concluídas; o gestor acessa a pendência filtrada; o acesso aos módulos continua submetido à API.

## Melhorias técnicas que sustentam o redesign

| Prioridade | Mudança | Evidência e direção |
| --- | --- | --- |
| P0 | Tornar a escolha do SDK reproduzível | `global.json` exige 10.0.400 com `latestPatch`; README exige SDK 8; `server-tests` instala 8.0.x. Um runner sem 10.0.4xx compatível falha na seleção do SDK. Alinhar bootstrap, CI e imagens de build; garantir runtime 8 para os testes `net8.0` enquanto esse alvo existir |
| P1 | Separar resumo, feed e consultas por módulo | Portaria obtém lista de visitas sem paginação e feed limitado; dashboard global executa várias agregações sequenciais. Medir payload, número de consultas e latência antes de otimizar; paginar coleções e agrupar agregações compatíveis |
| P1 | Evoluir a leitura de estrutura e credenciais | `LicenseStructureController.GetStructure` inclui unidades e moradores; `CredentialManagementController.GetCredentials` materializa a lista completa. Projetar contagens e resumos, buscar detalhes sob demanda e paginar a base de credenciais |
| P1 | Coordenar recargas e troca de contexto | `Concierge.razor` recarrega a cada 15 segundos mesmo com SignalR e também em notificações. Coalescer atualizações, impedir resposta de consulta antiga de substituir contexto novo e manter atualização de recuperação após reconexão |
| P1 | Criar contrato compacto de contexto e capacidades | `LicenseWorkspace.OnParametersSetAsync` consulta licença e depois administração para descobrir permissões. Um resumo de contexto com permissões e módulos evita depender do payload administrativo para desenhar toda página |
| P1 | Testar comportamento de interface | `Condotify.Web.Tests` tem testes de login, logout, refresh, validação e assistente; não foi encontrada suíte de interação dos componentes no conjunto inspecionado. Acrescentar testes de componente e jornadas de navegador para portaria, contexto e degradação de conexão |
| P2 | Consolidar o sistema visual | `brand.css`, `portal.css`, `design-system.css`, `CondotifyTheme.cs` e o CSS mobile dividem definições. Portal carrega os três CSS em sequência. Estabelecer uma fonte de tokens e migrar por módulo para CSS isolado, removendo regras substituídas apenas após validação visual |
| P2 | Separar responsabilidades grandes | `CondotifyApiClient.cs`, `Concierge.razor`, controladores e drivers concentram responsabilidades diferentes. Dividir cliente por domínio e extrair estado da portaria e normalização de capacidades de hardware, preservando o contrato público durante a migração |

Não paralelizar consultas no mesmo `DbContext`. Usar projeções e agregações primeiro; só considerar contextos independentes e cache depois de medir. Chaves de cache devem incluir tenant e escopo de autorização. Expiração de permissões deve invalidar o acesso, não apenas o menu.

As filas, workers, fingerprints de alertas, idempotência e infraestrutura de autorização já existem. É preferível integrá-los à experiência proposta a criar mecanismos concorrentes para a mesma operação.

## Direção visual

- Preservar azul `#092557` e verde `#7BC053` da F&F Access. Reservar verde de estado para confirmações observadas e vermelho para situações que exigem atenção.
- Cabeçalhos menores, título do trabalho atual, condomínio sempre visível e uma ação principal por contexto.
- Listas de operação com densidade ajustável, estados por texto e ícone, detalhes sem perda de contexto e hierarquia tipográfica consistente.
- Estados de carregamento, erro, vazio, falta de permissão e dados antigos com mensagem e recuperação próprias.
- Ações frequentes rotuladas; nomes acessíveis para ícones; navegação por teclado; foco restaurado depois de diálogos; anúncios de resultado sem deslocar o foco.
- Validar largura de 360 px, 736 px e 1.024 px, zoom e contraste. No app real, validar também leitor de tela e dispositivos móveis.

Mensagens de resultado devem ser identificáveis por tecnologia assistiva sem mudança de foco, conforme [WCAG 2.2, mensagens de status](https://www.w3.org/WAI/WCAG22/Understanding/status-messages). Isso orienta o aceite; esta revisão não certifica conformidade do produto.

## Ordem de implementação sugerida

Estimativa relativa: P = pequena, M = média, G = grande. Não equivale a prazo fechado.

| Entrega | Tamanho | Dependências | Verificação principal |
| --- | --- | --- | --- |
| 1. Indicadores, estados de conexão e seleção de SDK | M | Definir semântica de “hoje”, “online” e “sem dados” | Casos de falha, dia com >80 eventos e execução em runner limpo |
| 2. Contexto de condomínio e componentes de estado | M | Contrato de capacidades e decisão de navegação | Troca rápida de condomínio, links profundos e permissões |
| 3. Nova fila de portaria e detalhes contextuais | G | Entregas 1–2; modelo de responsável se adotado | Operação com teclado, concorrência e reconexão |
| 4. Credenciais e acompanhamento por equipamento | M/G | Definição de cadastro sem distribuição por tipo | Falha parcial, expiração, suspensão e recuperação idempotente |
| 5. Rotina do morador e resumo do gestor | M/G | Endpoints de resumo específicos por público | Dados reais, módulos desabilitados, cache e horários |
| 6. Consolidação visual e extrações técnicas restantes | M | Componentes estabilizados nas entregas anteriores | Comparação visual e testes das jornadas preservadas |

Entregar cada etapa em PR pequeno e ativar mudanças maiores por módulo. Em homologação, validar com dados sintéticos e equipamentos de teste. Medir antes/depois: tempo para tratar uma pendência, passos até a ação, erros e retrabalho, latência p95, payload, consultas e recargas. Essas métricas são propostas de avaliação; não foram medidas nesta revisão.

## Validação desta análise

- Branch criada a partir do commit local `3fff2e6`, sem consulta de atualização ao remoto.
- Inventário, leitura de código, CI e estilos concluídos. Nenhum `AGENTS.md` foi encontrado na árvore revisada ou nos diretórios pais verificados.
- Execução e resultado dos testes locais: ver `validation.md` nesta pasta.
- Os problemas listados em P0 foram identificados por leitura das condições e consultas; não são relatos de incidentes confirmados em produção.
- O mockup explora portaria, distribuição de credenciais e início do morador. Funcionalidades de atendimento e resumos adicionais dependem das implementações descritas acima.

Na configuração de CI, a conclusão é uma inferência entre arquivos locais e as regras de seleção descritas pela [documentação de global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json). A ação permite instalar o SDK indicado no arquivo usando [`global-json-file`](https://github.com/actions/setup-dotnet#using-the-global-json-file-input). Não foi consultado o resultado atual do GitHub Actions.
