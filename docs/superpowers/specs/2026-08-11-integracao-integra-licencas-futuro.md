# Integração do Integra com o controle financeiro das licenças

**Data:** 11/08/2026
**Status:** decisão registrada para implementação futura — não implementar nesta etapa.

## Objetivo

Integrar o Condotify ao Integra, sistema próprio de controle financeiro, para acompanhar o faturamento comercial das licenças da plataforma sem transformar o Condotify em um processador de pagamentos.

Esta integração é diferente e deve permanecer separada do financeiro dos moradores e das unidades existente dentro de cada condomínio.

## Fonte oficial dos dados

- O **Integra** será a fonte oficial de clientes, contratos, planos, mensalidades, vencimentos, pagamentos, negociações e situação financeira.
- O **Condotify** continuará responsável por condomínios, licenças, módulos, usuários, equipamentos, credenciais e aplicação da política operacional.
- O Condotify manterá somente uma projeção local dos dados financeiros necessários para consulta e controle da licença.
- Preços, baixas, pagamentos e negociações não serão alterados pelo Condotify.

## Modelo de integração aprovado

1. O Condotify cria ou vincula uma licença a um contrato do Integra.
2. O vínculo utiliza identificadores externos estáveis, incluindo `LicenseId`, `IntegraCustomerId` e `IntegraContractId`.
3. O Integra envia webhooks quando contrato, parcela ou situação financeira mudar.
4. O Condotify processa cada evento de forma idempotente e atualiza sua projeção local.
5. Um processo periódico de reconciliação consulta o Integra e corrige eventos eventualmente não recebidos.
6. O portal apresenta as informações em modo gerencial e oferece um link para abrir o registro correspondente no Integra.

Não será permitido acoplamento por escrita direta no banco do Integra ou do Condotify. A comunicação ocorrerá por API e eventos autenticados.

## Dados locais previstos

Criar um modelo complementar à licença, sem sobrecarregar `LicenseDTO.ExpireDate`, contendo pelo menos:

- identificadores do cliente e contrato no Integra;
- código e versão do plano contratado;
- situação financeira e contratual;
- próximo vencimento e competência atual;
- data do último pagamento conhecida;
- término do período de tolerância;
- data e resultado da última sincronização;
- versão do último evento processado;
- motivo de pendência, suspensão ou cancelamento;
- divergências entre plano contratado, módulos liberados e métricas de uso.

Também deverão existir uma caixa de entrada idempotente de eventos, histórico de sincronização e fila de falhas com retentativa.

## Política operacional de inadimplência

A situação financeira nunca deve desligar automaticamente portas, equipamentos, rotas de emergência ou credenciais existentes.

- **Em dia:** operação normal.
- **Vencimento próximo:** aviso administrativo e comercial.
- **Em atraso:** alertas no portal, sem interrupção da operação.
- **Tolerância encerrada:** possibilidade de restringir alterações administrativas e novos cadastros, conforme política configurável.
- **Suspensa:** preserva portaria, emergências, abertura de portas e credenciais existentes.
- **Cancelada:** encerramento confirmado, exportação e procedimento de desligamento controlado antes da desativação definitiva.

O Integra informa o estado financeiro; o Condotify decide e audita o efeito operacional de acordo com uma política segura e versionada.

## Portal futuro

Criar uma área global chamada **Assinaturas e licenças**, separada dos módulos financeiros dos condomínios, contendo:

- licenças em dia, próximas do vencimento, em atraso e suspensas;
- receita recorrente e vencimentos informados pelo Integra;
- plano e módulos contratados;
- histórico financeiro da licença;
- situação e horário da última sincronização;
- divergências de plano, módulos ou métricas de uso;
- ações `Sincronizar agora` e `Abrir no Integra`;
- auditoria das alterações de situação e das decisões operacionais.

## Segurança e confiabilidade

- autenticação servidor a servidor por OAuth2 Client Credentials, mTLS ou mecanismo equivalente;
- webhooks assinados com HMAC, timestamp e proteção contra repetição;
- chave idempotente obrigatória por evento;
- segredos separados por ambiente e mantidos fora do repositório;
- retentativa exponencial e fila de mensagens não processadas;
- reconciliação periódica;
- auditoria sem armazenamento de segredos ou payloads sensíveis em texto aberto;
- envio mínimo de dados: nenhuma informação de moradores deve ser compartilhada com o Integra.

## Estratégia de implantação futura

1. Documentar ou criar a API REST e os webhooks do Integra.
2. Implementar o vínculo entre licença e contrato.
3. Implantar sincronização somente de leitura e a tela gerencial.
4. Executar reconciliação paralela e comparar os resultados entre os sistemas.
5. Somente após validação operacional, habilitar avisos e restrições graduais.
6. Nunca iniciar a implantação com suspensão automática.

## Pendências para retomada

Antes de elaborar o plano de implementação, confirmar:

- se o Integra já possui API REST e webhooks;
- método de autenticação disponível;
- identificador estável do cliente e do contrato;
- estados reais de contrato e cobrança usados pelo Integra;
- regras comerciais de tolerância, suspensão e cancelamento;
- se o plano é fixo ou calculado por módulos, moradores, unidades ou equipamentos;
- quais valores financeiros podem ser exibidos no Condotify;
- URL segura para deep link do Condotify para o Integra.
