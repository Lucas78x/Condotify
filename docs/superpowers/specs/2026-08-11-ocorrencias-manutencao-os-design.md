# Ocorrências, manutenção e ordens de serviço

**Status:** aprovado em 11/08/2026.

## Objetivo

Transformar o módulo atual de ocorrências em uma central completa de atendimento e manutenção para equipe e moradores, cobrindo abertura, triagem, ordem de serviço, SLA, prestadores, custos informativos, manutenção preventiva, evidências e histórico.

## Disponibilidade por condomínio

O sistema inteiro é governado pelo módulo existente `Incidents` (`Ocorrências`) da licença. Não haverá um segundo toggle concorrente.

- Quando ativo, portal e aplicativo exibem ocorrências, ordens de serviço e manutenção preventiva conforme as permissões do usuário.
- Quando inativo, as rotas deixam de aparecer e a API rejeita consultas e mutações do módulo.
- Desativar nunca apaga ocorrências, ordens, anexos, planos ou histórico.
- Ao reativar, o estado anterior volta a ficar disponível.
- A lista global só considera condomínios com o módulo ativo.

## Modelo operacional

- **Ocorrência:** chamado inicial, aberto pela equipe, automação ou morador.
- **Ordem de serviço:** execução do atendimento, vinculada a uma ocorrência ou gerada por um plano preventivo.
- **Plano preventivo:** recorrência configurada para equipamento/local, com próxima execução e checklist modelo.
- **Prestador:** contato operacional externo, especialidade e estado ativo/inativo.
- **Política de SLA:** prazos de primeira resposta e solução por gravidade.

Custos são exclusivamente informativos. A plataforma não fará transações financeiras.

## Fluxo

1. Morador ou equipe registra título, descrição, categoria, gravidade e localização; fotos são opcionais.
2. A ocorrência entra em triagem com SLA calculado.
3. A equipe assume, atribui responsável/prestador e pode gerar uma ordem de serviço.
4. A ordem percorre planejada, atribuída, em execução, aguardando terceiro/material e concluída.
5. Checklist, custos, comentários, anexos e transições ficam auditáveis.
6. Ao concluir a ordem, a equipe pode resolver a ocorrência vinculada.
7. Planos preventivos geram ordens automaticamente, sem criar duplicatas para a mesma competência.

## Portal

A página será um painel operacional responsivo com:

- indicadores de chamados abertos, críticos, SLA em risco/atrasado e preventivas próximas;
- abas `Painel operacional`, `Preventivas` e `Prestadores`;
- quadro de trabalho por situação e filtros por prioridade, responsável, prestador e busca;
- detalhe lateral com SLA, localização, responsável, custos, checklist e linha do tempo;
- criação e edição em diálogos próprios;
- estado vazio orientando o primeiro cadastro.

## Aplicativo

- Morador: abrir chamado guiado, anexar fotos, informar localização e acompanhar somente chamados próprios.
- Equipe: consultar fila, assumir atendimento, atualizar situação, checklist e comentários.
- O acesso aparece em `Mais` somente com o módulo ativo.
- O aplicativo traduz enumerações e mensagens para português; não expõe nomes internos.

## Segurança e privacidade

- Equipe exige `ViewIncidents` para leitura e `ManageIncidents` para alterações.
- Morador é resolvido pelo vínculo autenticado e só acessa registros que ele mesmo abriu.
- Todas as entidades persistentes carregam `LicenseId` e participam do filtro de tenant.
- Anexos são servidos por endpoint autorizado; caminhos físicos não são públicos.
- Comentários podem ser marcados como visíveis ao morador; notas internas permanecem privadas.
- Alterações importantes também geram auditoria operacional.

## Critérios de aceite

- Desativar o módulo bloqueia portal, aplicativo e API sem perda de dados.
- Morador consegue abrir e acompanhar um chamado próprio, mas não vê chamados de terceiros.
- Equipe consegue criar, atribuir e concluir ordem com checklist e custos informativos.
- SLA é calculado e sinaliza risco/atraso.
- Plano preventivo gera uma única ordem por competência.
- Build, testes, migração e testes visuais passam.
