# Integrações Google e Apple Wallet

As credenciais são configuradas no Portal em **Condomínio → Administrar → Carteiras digitais**. A configuração pertence à empresa e é reutilizada por todas as licenças dela.

## Cofre

Antes de salvar credenciais, configure na API uma chave exclusiva com pelo menos 32 caracteres:

```text
CONDOTIFY_WALLET_SECRET=<segredo aleatório exclusivo>
```

Não reutilize `JWTCondotify_Secret`, `CONDOTIFY_EQUIPMENT_SECRET` ou `CONDOTIFY_MEDIA_SECRET`. O valor não fica no banco nem deve entrar no repositório. O banco armazena apenas valores AES-GCM protegidos e vinculados à empresa e à finalidade da credencial.

## Google Wallet

Há dois modos:

- **Chave protegida:** informe Issuer ID, e-mail da conta de serviço, class suffix e cole somente a chave privada PEM. O JSON da service account não é necessário e a chave não volta ao navegador depois de salva.
- **Identidade gerenciada:** a máquina ou workload da API deve possuir Application Default Credentials e permissão `iam.serviceAccounts.signJwt` na conta informada. A assinatura é feita pelo Google e nenhuma chave privada é armazenada pelo Condotify.

Em ambos os modos, salvar executa uma assinatura de teste. A integração só pode ser ativada depois da validação.

## Apple Wallet

Informe Pass Type Identifier, Team Identifier, senha e selecione uma vez o certificado `.p12` ou `.pfx` do Pass Type ID. A API baixa o certificado intermediário WWDR G4 diretamente da Apple, valida a chave privada e realiza uma assinatura PKCS#7 de teste.

O Portal recebe somente fingerprint mascarado, vencimento, status e datas de validação. O `.p12/.pfx` e sua senha nunca são devolvidos.

## Rotação

Ao substituir uma chave ou certificado, a integração é desativada. Valide a nova versão e só então reative. Configuração, ativação e desativação são registradas na auditoria operacional sem incluir segredos.
