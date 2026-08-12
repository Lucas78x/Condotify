# Financeiro gerencial: recorrência, importação e lembretes

## Objetivo

Evoluir a gestão financeira sem transformar o Condotify em instituição de pagamento. A plataforma registra lançamentos, gera cobranças recorrentes, importa planilhas e comunica moradores, mas nunca recebe, liquida ou transfere valores.

## Escopo

- Regras mensais por todas as unidades ou por unidades selecionadas.
- Geração idempotente por regra, competência e unidade.
- Importação CSV compatível com Excel, com prévia obrigatória e confirmação explícita.
- Régua configurável antes do vencimento, no vencimento e após o vencimento.
- Push e e-mail sem exposição do valor; detalhes apenas na área autenticada.
- Histórico imutável das entregas, tentativas e falhas.
- Execução automática em worker e verificação manual auditada.

## Decisões de segurança

- Nenhum endpoint de pagamento, PIX, cartão, saldo ou conta bancária.
- Isolamento por condomínio em todas as novas entidades.
- Chaves únicas impedem geração, importação e comunicação duplicadas.
- E-mail usa o SMTP já configurado no condomínio ou no ambiente.
- Importações com qualquer linha inválida não podem ser confirmadas.
- O aplicativo recebe somente informações pertencentes às unidades ativas do morador.

## Formato da planilha

CSV UTF-8, separado por ponto e vírgula ou vírgula, com até 1.000 linhas e 2 MB. Colunas: Bloco, Unidade, Competencia, Referencia, Descricao, Vencimento, Valor, Multa, Juros, Desconto e Observacoes.
