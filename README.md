# Condotify

Plataforma de gestao condominial com portal Blazor Server/MudBlazor, API ASP.NET Core, PostgreSQL e Entity Framework Core.

## Requisitos

- .NET SDK 8
- Docker Desktop

## Executar localmente

1. Inicie o banco:

   ```powershell
   docker-compose up -d postgres
   ```

2. Inicie a API:

   ```powershell
   dotnet run --project CondotifyAPI
   ```

3. Em outro terminal, inicie o portal:

   ```powershell
   dotnet run --project Condotify
   ```

4. Acesse `https://localhost:7064`.

Em desenvolvimento, as migrations sao aplicadas automaticamente e o banco recebe um ambiente demonstrativo.

## Acesso de desenvolvimento

- E-mail: `teste@condotify.local`
- Senha: `Teste@123`

## Enderecos

- Portal HTTPS: `https://localhost:7064`
- Portal HTTP: `http://localhost:5035`
- API/Swagger: `https://localhost:7118/swagger`
- PostgreSQL: `localhost:5432`

## Verificacao

```powershell
dotnet build Condotify.sln
dotnet test CondotifyAPI.Tests
```

O portal centraliza a comunicacao com a API em `CondotifyApiClient`; os componentes nao montam requisicoes HTTP individualmente. A sessao web usa cookie protegido e mantem o JWT da API dentro do ticket autenticado.

## Credenciais e controladores de acesso

No ambiente de um condominio, os modulos `Credenciais` e `Acessos` permitem:

- vincular facial, cartao, QR Code, tag, tag veicular e senha/PIN a um morador;
- sincronizar e restaurar uma credencial em um ou mais equipamentos;
- ativar ou suspender a credencial sem apagar o cadastro central;
- iniciar ou cancelar a captura facial no terminal Control iD;
- remover somente o vinculo de um equipamento;
- consultar eventos autorizados e negados diretamente no controlador;
- acompanhar operacoes pendentes e o historico de auditoria.

Uma falha de rede nao descarta o cadastro: o vinculo fica pendente para restauracao posterior. Fotos enviadas pelo portal sao usadas somente na requisicao ao equipamento e nao sao armazenadas pelo Condotify.

### Compatibilidade atual

- Control iD iDFace/iDFace Max: usuario, foto facial, captura remota, cartao, QR Code, UHF, PIN, status e logs.
- Intelbras facial: usuario, foto facial, cartao/tag, senha, status e logs. A foto deve ter ate 100 KB.
- Intelbras UHF: usuario, cartao/tag, senha, status e logs.
- Hikvision: driver ainda nao habilitado; requer documentacao ou equipamento de homologacao antes de liberar operacoes reais.
