# Deploy com Docker Compose e Caddy

O arquivo `docker-compose.yml` da raiz e o unico ponto de entrada. Ele inclui:

- `deploy/proxy/compose.yaml`: Caddy, dono exclusivo das portas 80 e 443 e
  responsavel por HTTPS automatico;
- `deploy/condotify/compose.yaml`: portal, API, PostgreSQL, MediaMTX e OCR.

As variaveis compartilhadas ficam no `.env` da raiz. Certificados e chaves do
Caddy ficam no volume nomeado `condotify_caddy-data` e nao devem ser removidos.

## Primeira instalacao

Com Docker e Docker Compose ja instalados:

```bash
cd /opt/condotify
./deploy/bootstrap-vps.sh fefaccess.grupoff.net.br <porta-ssh>
```

## Atualizacao

```bash
cd /opt/condotify
./deploy/deploy-vps.sh
```

O script interrompe a atualizacao se o checkout estiver em outro branch, se
existirem alteracoes nao comitadas ou se o remoto exigir merge/rebase. Assim, o
codigo executado na VPS sempre corresponde a um commit identificavel no Git. O
comando tambem aguarda e confirma explicitamente os health checks da API,
portal, banco e proxy; uma
atualizacao que nao ficar pronta retorna erro em vez de aparentar sucesso.

Para implantar outro branch de forma explicita:

```bash
CONDOTIFY_DEPLOY_BRANCH=main ./deploy/deploy-vps.sh
```

Para validar ou recarregar apenas o proxy:

```bash
docker compose exec caddy caddy validate --config /etc/caddy/Caddyfile
docker compose exec -w /etc/caddy caddy caddy reload --config Caddyfile
```

## Backup PostgreSQL

O backup operacional usa `pg_dump` no formato custom, valida o arquivo e faz
uma restauracao completa em um banco temporario antes de considera-lo valido:

```bash
./deploy/backup-postgres.sh
./deploy/install-backup-cron.sh
```

Por padrao sao mantidos 7 dias, 5 semanas e 13 meses. Para copia externa,
configure um remote do `rclone` e defina `CONDOTIFY_BACKUP_OFFSITE_REMOTE` no
ambiente do cron. O restore exige confirmacao explicita e aceita somente dumps
com checksum dentro do diretorio oficial:

```bash
CONDOTIFY_ALLOW_DATABASE_RESTORE=yes ./deploy/restore-postgres.sh backups/postgres/daily/ARQUIVO.dump
```

## Deploy de producao

O CI testa API, portal, cliente compartilhado, nucleo mobile, OCR e Compose. So
depois publica no GHCR as tres imagens com a tag `sha-COMMIT`. A VPS deve estar
autenticada uma unica vez para leitura dos pacotes privados:

```bash
echo "$GHCR_READ_TOKEN" | docker login ghcr.io -u USUARIO --password-stdin
./deploy/deploy-vps.sh
```

O script recusa arvore Git suja, baixa as imagens antes da janela de manutencao,
confere o commit gravado em cada imagem e fixa os digests. Em seguida para novas
escritas, cria e restaura um backup de verificacao, executa a migracao como tarefa
unica e troca os containers. Falha de migracao ou health check restaura banco e
imagens anteriores automaticamente. A API de producao nunca executa migracoes ao
iniciar.

Configure `ci-gate` como status check obrigatorio na regra da branch de producao.
Ele so aprova quando testes, Compose e as tres imagens terminam com sucesso.
Restrinja tambem force-push e exclusao da branch.

## Protecao da branch de producao

A branch implantada atualmente e `feature/ff-access-branding`. No GitHub, acesse
`Settings > Rules > Rulesets`, crie um `New branch ruleset` e use estas opcoes:

- nome: `Production protection`;
- status: `Active`;
- branch alvo por padrao: `feature/ff-access-branding`;
- bypass list: vazia;
- `Restrict deletions`: habilitado;
- `Block force pushes`: habilitado;
- `Require a pull request before merging`: habilitado;
- aprovacoes obrigatorias: zero enquanto houver apenas um mantenedor;
- `Require conversation resolution before merging`: habilitado, quando
  disponivel;
- `Require status checks to pass before merging`: habilitado;
- status check obrigatorio: `ci-gate`, com origem `GitHub Actions`;
- `Require branches to be up to date before merging`: habilitado.

O nome do check e exatamente `ci-gate`, nao o nome do workflow `ci` nem os jobs
individuais. Esse job agregador falha se qualquer teste, validacao do Compose ou
construcao de imagem falhar.

### Fluxo de alteracao e deploy

Depois que o ruleset estiver ativo, nao envie commits diretamente para a branch
de producao:

1. Atualize a branch de producao local e crie uma branch para a alteracao.
2. Desenvolva, teste, faça commit e publique essa nova branch.
3. Abra um Pull Request para `feature/ff-access-branding`.
4. Aguarde o `ci-gate` concluir com sucesso e resolva as conversas do PR.
5. Faça o merge do Pull Request.
6. Aguarde o workflow disparado pelo merge terminar com sucesso. A execucao do
   Pull Request apenas valida as imagens; a execucao posterior ao merge publica
   no GHCR as imagens `sha-COMMIT` usadas pela VPS.
7. Na VPS, execute `./deploy/deploy-vps.sh` somente depois que as imagens do
   commit resultante do merge tiverem sido publicadas.

Se a branch de producao for alterada futuramente, atualize em conjunto o alvo do
ruleset, os gatilhos de `.github/workflows/ci.yml` e `CONDOTIFY_DEPLOY_BRANCH`.

Somente `caddy` publica TCP 80/443. O MediaMTX publica UDP 8189 para o ICE do
WebRTC. Banco, portal, API, HLS e sinalizacao WebRTC nao publicam portas no host.

O Nginx e o Certbot legados podem permanecer instalados durante a janela de
rollback, mas seus servicos devem ficar desabilitados e sem sockets abertos.
