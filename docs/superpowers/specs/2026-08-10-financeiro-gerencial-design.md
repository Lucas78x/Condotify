# Financeiro gerencial — desenho técnico

## Objetivo

Adicionar ao Condotify controle de cobranças, inadimplência e manifestações do morador sem receber, transferir, custodiar ou liquidar dinheiro. O módulo é um livro gerencial e informativo; o pagamento sempre acontece fora da plataforma.

## Limites obrigatórios

- Não existe botão "Pagar agora", carteira, checkout, PIX, cartão ou conta bancária.
- Uma baixa é apenas um registro administrativo, sempre com ator, data e histórico.
- Valores usam `decimal(18,2)` e nunca `float`/`double`.
- Atraso é derivado da data de vencimento; não depende de rotina noturna para permanecer correto.
- Eventos financeiros são imutáveis. A cobrança pode mudar, o histórico não.
- O morador acessa somente cobranças de unidades com vínculo ativo na licença do token.
- Toda consulta de equipe exige `ViewFinance`; toda alteração exige `ManageFinance`.
- Notificações não expõem valor ou inadimplência na tela bloqueada.

## Modelo

`FinancialCharge` representa um lançamento por unidade: competência, referência, descrição, vencimento, valor base, multa, juros, desconto, estado e eventual documento de boleto. `FinancialChargeEvent` registra criação, edição, manifestação, confirmação, rejeição, contestação, negociação, cancelamento e reabertura.

Estados persistidos: em aberto, pagamento informado, pago, negociado, contestado e cancelado. "Vencido" é uma apresentação derivada quando um estado não terminal ultrapassa o vencimento.

## Fluxos

### Administração

1. Cria uma cobrança para uma ou várias unidades.
2. Acompanha arrecadação registrada, valores em aberto, faixas de atraso e manifestações.
3. Confirma/rejeita pagamento informado, registra negociação, cancela ou reabre.
4. Consulta a trilha completa de cada lançamento.

### Morador

1. Consulta situação consolidada das próprias unidades.
2. Abre o documento associado, quando houver.
3. Informa que pagou fora da plataforma ou contesta o lançamento.
4. Acompanha a análise da administração.

## Segurança e consistência

- Criação em lote recebe chave idempotente e gera uma chave por unidade.
- Alterações são transacionais: cobrança e evento são gravados no mesmo `SaveChanges`.
- Cancelamento e reabertura não apagam dados.
- Consultas administrativas são isoladas por licença e as do morador por licença + unidades ativas.
- O histórico guarda somente texto operacional limitado, nunca credenciais bancárias.
