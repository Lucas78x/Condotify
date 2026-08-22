# Condotify Mobile - configuracao e publicacao

## API

Defina `CONDOTIFY_API_BASE_URL` antes de iniciar ou publicar o aplicativo. O Android Emulator usa `http://10.0.2.2:5093` por padrao; Windows, iOS e Mac Catalyst usam `https://localhost:7118` em desenvolvimento.

O aplicativo nunca armazena senha. Access token, refresh token e identidade da sessao ficam no `SecureStorage` da plataforma.

Builds Android `Release` exigem `CondotifyApiBaseUrl` explicita e com HTTPS, alem da chave de upload da Play (`AndroidKeyStore=true`). O manifesto padrao bloqueia todo trafego em texto claro. Para desenvolvimento local via HTTP, o comando deve selecionar explicitamente `Platforms\Android\AndroidManifest.Debug.xml`; esse manifesto nunca e selecionado pelo build padrao.

## Firebase Cloud Messaging

1. Crie os aplicativos Android (`br.com.condotify.app`) e iOS no mesmo projeto Firebase.
2. Coloque `google-services.json` em `Condotify.Mobile/Platforms/Android/`.
3. Coloque `GoogleService-Info.plist` em `Condotify.Mobile/Platforms/iOS/`.
4. Na VPS, salve o JSON de conta de servico como `secrets/condotify-firebase-service-account.json`. O Compose o monta em `/run/secrets/condotify-firebase-service-account.json` e configura `GOOGLE_APPLICATION_CREDENTIALS` na API. Se necessario, altere o caminho do host com `FIREBASE_SERVICE_ACCOUNT_PATH`.
5. `CONDOTIFY_FCM_PROJECT_ID` e opcional; quando omitido, a API usa o `project_id` presente na conta de servico.
6. No Firebase, vincule uma chave APNs ao aplicativo iOS. O entitlement `aps-environment` deve ser alterado de `development` para `production` no perfil de distribuicao.

Os arquivos de credencial nao devem ser versionados. Sem eles, o aplicativo continua funcional, mas o transporte push permanece indisponivel e o worker aplica retry/dead-letter sem reverter a operacao principal.

## App Links e Universal Links

A API publica os documentos abaixo somente quando os valores reais forem configurados:

- `https://fefaccess.grupoff.net.br/.well-known/assetlinks.json`
- `https://fefaccess.grupoff.net.br/.well-known/apple-app-site-association`

Configure:

- `MOBILE_LINKS_ANDROID_PACKAGE_NAME=br.com.condotify.app`
- `MOBILE_LINKS_ANDROID_SHA256_FINGERPRINT_0=<SHA-256 do certificado de assinatura do app na Play Console>`
- `MOBILE_LINKS_APPLE_TEAM_ID=<Apple Team ID>`
- `MOBILE_LINKS_APPLE_BUNDLE_ID=br.com.condotify.app`

O proxy do dominio deve encaminhar `/.well-known/*` para a API sem autenticacao e preservar `Content-Type: application/json`. Links aceitos usam `https://fefaccess.grupoff.net.br/app/*`; qualquer host, porta, query, fragmento ou rota fora da allowlist e recusado pelo app. No Android com Play App Signing, use o SHA-256 do certificado de assinatura do app, e nao o certificado da chave de upload.

## Builds

```powershell
dotnet test Condotify.Mobile.Tests\Condotify.Mobile.Tests.csproj
dotnet build Condotify.Mobile\Condotify.Mobile.csproj -f net10.0-android36.0
dotnet build Condotify.Mobile\Condotify.Mobile.csproj -f net10.0-windows10.0.19041.0
```

O build iOS/Mac Catalyst e a assinatura para App Store exigem um Mac com Xcode, conta Apple Developer, certificado e provisioning profile reais. O build Android de loja exige keystore de producao; esses artefatos ficam fora do repositorio.

Um Release destinado somente a homologacao fisica pode dispensar temporariamente a chave de upload com `-p:CondotifyAllowNonStoreSigning=true`. Esse artefato recebe assinatura de desenvolvimento e nunca deve ser enviado ao Play Console.

Para gerar um APK de teste para um aparelho fisico, informe o endereco da API acessivel na rede local:

```powershell
dotnet publish Condotify.Mobile\Condotify.Mobile.csproj -f net10.0-android36.0 -c Debug -p:AndroidPackageFormat=apk -p:EmbedAssembliesIntoApk=true -p:AndroidManifest=Platforms\Android\AndroidManifest.Debug.xml -p:CondotifyApiBaseUrl=http://192.168.0.10:5093
```

O endereco fica incorporado somente naquele artefato. Quando a propriedade nao e informada, o Android continua usando `http://10.0.2.2:5093` para o emulador.

## Privacidade residente

- Encomendas aparecem ao morador somente quando possuem `RecipientResidentId` e `UnitId` vinculados a uma unidade ativa da sessao.
- Cameras aparecem somente com `ResidentVisible=true`.
- As rotas residentes de CFTV nunca retornam IP, portas, usuario ou senha do equipamento.
- A API revalida o vinculo e a visibilidade antes de listar, abrir ou encerrar uma sessao de video.
- Snapshots sao obtidos pela API, limitados a 5 MB e devolvidos como imagem; falhas de compatibilidade ou camera offline exibem um placeholder sem travar a listagem.
