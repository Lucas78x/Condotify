# Implementação do redesign

Branch: `design/project-review-and-redesign` · base: `3fff2e6` · 07/09/2026.

Código em `D:\repos\Condotify`. Esta é a cópia de trabalho criada a partir do repositório informado. A pasta `D:\source\repos\Condotify` permanece na branch original. Não houve publicação ou execução contra equipamentos reais.

## Entregue

| Área | Mudança implementada |
| --- | --- |
| Portaria | Fila com seleção estável, contexto, próxima ação e resolução auditada pelo endpoint existente; aprovação e saída preservam as confirmações existentes. Pendências têm consulta própria e paginação de 20 eventos, sem depender do feed de 80 registros. |
| Conexão | Distinção entre SignalR conectado, reconectando, consulta periódica e dados antigos; última consulta visível. Requisições de atualização são agrupadas, respostas de contextos anteriores são descartadas e ações da fila ficam bloqueadas com dados antigos. |
| Credenciais | Lista resumida e painel de distribuição por equipamento, com tentativas, último sucesso, próxima tentativa e ação contextual. Cadastro central sem distribuição imediata para QR, cartão, tag, tag veicular e PIN. Cadastro facial continua exigindo equipamento e segue a política de imagem existente. |
| Contexto | Seletor no cabeçalho, escolha salva na sessão do navegador e validada contra os condomínios acessíveis. Links de cadastros, credenciais, equipamentos e portaria acompanham a escolha; a URL do condomínio prevalece. Trocas de contexto recriam os módulos e descartam consultas anteriores. |
| Morador | Componente compartilhado com encomendas recebidas para retirada, visitas de hoje, reservas a partir de hoje e cobranças abertas. Novo `GET /api/resident/home` com autorização atualizada, contagens no banco e respeito aos módulos habilitados. Falha de consulta mostra indisponibilidade; o cache mantém sua data e o aviso existente de dados salvos. |
| Gestão | Atalhos por permissão e configuração inicial derivada dos cadastros de grupos, unidades e moradores; eliminado o progresso fixo de 25%. |
| Indicadores | Negativas contadas no dia local completo; tendência agrupada pelo fuso configurado; saúde de equipamentos considera comunicação nos últimos cinco minutos. Sincronização usa vínculos como denominador. Alertas abertos são contados antes do limite da lista. Ausência de dados/permissão não é apresentada como saúde confirmada. |
| Engenharia | SDK da CI alinhado ao `global.json`; CSS isolado para componentes novos; contrato e componente de resumo reutilizáveis; testes de escopo, limites de data, tradução PostgreSQL, cliente HTTP, contexto e renderização. |

O novo cadastro central não cria um envio imediato nem armazena biometria. Uma operação posterior de distribuição/reconciliação continua seguindo as regras existentes. O estado ativo no cadastro não equivale à confirmação de acesso no terminal.

## Validação

Resultados finais e limites em [validation.md](validation.md). A inspeção visual usou componentes reais do portal e da biblioteca compartilhada, com serviços HTTP fictícios em um host local de revisão. Isso valida a renderização e as interações observadas, sem representar homologação de banco ou hardware.

Capturas locais e logs ficam em `artifacts/project-review` (ignorado pelo Git). O host de revisão foi criado exclusivamente nessa pasta; não há endpoint de demonstração adicionado ao produto.

## Continuidade do projeto

O diagnóstico também registra investimentos maiores: endpoint compacto de contexto/permissões, paginação de todas as bases administrativas, divisão dos controladores e clientes grandes, métricas de uso e testes completos com PostgreSQL isolado/MAUI/hardware. Esses itens permanecem no roteiro técnico; não foram apresentados como concluídos nesta implementação.

A atribuição de responsável mostrada no protótipo era uma simulação. A implementação usa o fluxo existente de resolução auditada. Uma atribuição persistente entre operadores exigiria contrato e regras de concorrência próprios.

Nenhuma migração de banco foi adicionada. Para disponibilizar estas mudanças, atualizar a API junto com os clientes: campos novos de escopo são conservadores quando ausentes, e a tela do morador informa indisponibilidade se o endpoint novo ainda não estiver disponível.

## Integração

Destino do PR: `feature/ff-access-branding`. Antes da publicação, a branch de trabalho foi alinhada por fast-forward a `d46a971`, cujo conteúdo corresponde à base analisada. A decisão de merge permanece com o mantenedor.
