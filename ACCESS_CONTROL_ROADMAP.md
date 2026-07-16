# Plano de evolucao do controle de acesso

Este documento define a evolucao do Condotify para operar credenciais, rotas e
equipamentos com seguranca, rastreabilidade e recuperacao. O principio central e
separar tres estados:

1. estado desejado no Condotify;
2. estado confirmado em cada equipamento;
3. operacoes pendentes necessarias para reconciliar os dois estados.

## 1. Fundacao operacional

### Jobs e itens de operacao

Criar `AccessOperationJob` e `AccessOperationItem` para toda acao que possa
atingir um ou muitos equipamentos.

- Tipos: provisionar, suspender, reativar, excluir, restaurar e alterar rota.
- Estados: pendente, executando, concluido, falhou, cancelado e aguardando equipamento.
- Cada item registra equipamento, credencial, tentativa, erro resumido e horario.
- Toda operacao recebe uma chave de idempotencia para impedir duplicidade.
- Retentativas usam intervalo progressivo e limite configuravel.
- O usuario pode cancelar apenas itens ainda nao executados.
- O resultado fica vinculado ao log de auditoria.

### Reconciliacao

Quando o fabricante permitir leitura do inventario remoto, comparar a base do
Condotify com o equipamento e classificar cada vinculo como:

- sincronizado;
- pendente;
- divergente;
- ausente no equipamento;
- orfao no equipamento.

Uma restauracao deve nascer dessa comparacao. Nunca deve sobrescrever o terminal
inteiro sem uma simulacao e uma confirmacao explicita.

## 2. Rotas, portas e regras

### Entidades

`AccessRoute`

- nome, descricao, direcao e situacao;
- fuso horario;
- agenda padrao;
- politica de anti-passback;
- permite ou nao credenciais temporarias.

`AccessRouteDevice`

- rota, equipamento, portal/canal e papel;
- papeis: leitor de entrada, leitor de saida e acionador;
- prioridade e configuracao especifica do fabricante.

`AccessRouteRule`

- categoria de pessoa, unidade/grupo e tipo de credencial;
- dias da semana, faixas de horario e feriados;
- validade inicial e final;
- limite de acessos e sentido permitido.

### Capacidades dos drivers

Cada driver deve informar o que o equipamento realmente suporta:

- `GetCapabilitiesAsync`;
- `ListPortalsAsync`;
- `ReadCredentialInventoryAsync`;
- `ApplyAccessRouteAsync`;
- `ReadAccessRouteAsync`.

Assim, a interface mostra apenas canais e recursos validos para Control iD,
Intelbras ou futuros equipamentos, sem campos fixos como "Porta 1".

## 3. Temporarios

Unificar visitante, prestador, QR Code e face temporaria em uma politica de
acesso temporario.

- inicio e fim obrigatorios;
- responsavel e unidade de destino;
- rotas permitidas;
- quantidade maxima de acessos;
- QR Code de uso unico ou renovavel;
- limite e intervalo de renovacoes;
- face temporaria com retencao e exclusao automatica;
- suspensao imediata e expiracao executada pelo worker;
- alerta quando um equipamento estiver offline durante a expiracao.

O fim da validade altera primeiro o estado central e cria itens de remocao para
cada equipamento. Falhas permanecem visiveis ate a reconciliacao.

## 4. Backup e restauracao

### Snapshot versionado

Criar snapshots imutaveis por licenca e por equipamento contendo:

- pessoas, credenciais, rotas, agendas e vinculos;
- fabricante, modelo e versao do equipamento;
- versao do formato, data, autor e hash SHA-256;
- referencia para fotos em armazenamento de objetos, sem gravar Base64 no banco.

### Fluxo de restauracao

1. Selecionar snapshot e equipamentos de destino.
2. Executar pre-validacao de compatibilidade.
3. Gerar simulacao com inclusoes, alteracoes, remocoes e conflitos.
4. Exigir confirmacao para itens destrutivos.
5. Criar um job de restauracao com itens por credencial/equipamento.
6. Executar, verificar novamente no terminal e emitir relatorio final.

Modos previstos:

- restaurar uma credencial;
- restaurar uma pessoa;
- restaurar um equipamento;
- restaurar em massa por bloco, unidade, categoria ou rota;
- somente corrigir divergencias, sem remover itens desconhecidos.

## 5. Interface operacional

### Equipamento

A pagina do equipamento deve ter as abas:

- Visao geral;
- Rotas e portas;
- Credenciais;
- Saude e conectividade;
- Backup e restauracao;
- Logs e auditoria.

O cadastro de rota usa um assistente curto: origem/destino, portas, agenda,
publico permitido, temporarios e revisao.

### Central de operacoes

Adicionar uma central para jobs em massa com:

- filtros por licenca, bloco, unidade, pessoa, rota e equipamento;
- simulacao antes de executar;
- progresso geral e por equipamento;
- erros acionaveis, retentativa e cancelamento;
- exportacao do relatorio.

## 6. Ordem de entrega

### Fase 1 - Seguranca e observabilidade

- jobs, itens, idempotencia e retentativa;
- estados de sincronizacao e reconciliacao;
- auditoria sem expor senha, token ou stack trace.

### Fase 2 - Rotas e capacidades

- entidades de rota e agenda;
- descoberta de portas/canais por driver;
- configuracao de rotas no equipamento.

### Fase 3 - Backup e restauracao

- snapshots versionados;
- simulacao, restauracao individual e por equipamento;
- verificacao e relatorio final.

### Fase 4 - Operacao em massa

- selecao por filtros;
- provisionamento, suspensao, exclusao e restauracao em lote;
- painel de progresso e retentativas.

### Fase 5 - Temporarios avancados

- regras por rota, limite de usos e renovacoes;
- QR Code de uso unico;
- faces temporarias e limpeza verificavel nos terminais.

## Criterios de aceite

- Nenhuma falha de equipamento apaga silenciosamente o estado central.
- Toda acao remota possui autor, data, alvo, resultado e correlacao.
- Operacoes repetidas sao idempotentes.
- Restauracoes sempre possuem simulacao e relatorio.
- A interface diferencia estado desejado de estado confirmado.
- Recursos nao suportados pelo equipamento nao aparecem como disponiveis.
