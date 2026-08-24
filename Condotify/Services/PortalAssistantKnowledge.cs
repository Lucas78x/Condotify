using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Condotify.Services;

public sealed record PortalAssistantAction(string Label, string Url, string Icon);

public sealed record PortalAssistantReply(
    string Text,
    IReadOnlyList<PortalAssistantAction> Actions,
    string Topic);

public sealed record PortalAssistantSuggestion(string Label, string Question);

public sealed record PortalAssistantPageContext(
    string Path,
    string? WorkspaceKey,
    string? Section)
{
    public bool IsWorkspace => !string.IsNullOrWhiteSpace(WorkspaceKey);

    public static PortalAssistantPageContext FromUri(string uri)
    {
        var path = Uri.TryCreate(uri, UriKind.Absolute, out var absolute)
            ? absolute.AbsolutePath
            : uri.Split('?', '#')[0];
        path = $"/{path.Trim('/')}";

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0].Equals("condominios", StringComparison.OrdinalIgnoreCase))
        {
            var key = Uri.UnescapeDataString(segments[1]);
            var section = segments.Length >= 3 ? Uri.UnescapeDataString(segments[2]).ToLowerInvariant() : "visao-geral";
            return new(path, key, section);
        }

        return new(path, null, null);
    }

    public string WorkspaceUrl(string section) => IsWorkspace
        ? LicenseRoutes.Workspace(WorkspaceKey!, section)
        : "/licencas";
}

public sealed class PortalAssistantKnowledge
{
    private sealed record Intent(
        string Topic,
        string[] Phrases,
        string[] Keywords,
        Func<PortalAssistantPageContext, PortalAssistantReply> Reply);

    private static readonly Regex TokenSeparator = new("[^a-z0-9]+", RegexOptions.Compiled);

    private static readonly Intent[] Intents =
    [
        new("condominios",
            ["escolher condominio", "entrar no condominio", "trocar de condominio", "meus condominios", "varios condominios"],
            ["condominio", "condominios", "licenca", "licencas", "trocar", "escolher", "entrar"],
            context => Reply(
                "Em Condomínios você vê todos os ambientes aos quais sua conta tem acesso. Escolha um deles para abrir o espaço de gestão correspondente; a mesma conta pode participar de vários condomínios e você não precisa entrar novamente ao trocar entre eles.",
                "condominios",
                Action("Ver Condomínios", "/licencas", "apartment"))),

        new("portaria",
            ["portaria", "evento de acesso", "abrir porta", "visita na portaria", "porteiro"],
            ["portaria", "porteiro", "evento", "entrada", "saida", "porta", "visitante", "visita"],
            context => Reply(
                "Na Portaria você acompanha acessos recentes, identifica moradores e visitantes, consulta visitas previstas e registra entregas. Para abrir uma porta, selecione o evento ou dispositivo e confirme a ação; ela fica vinculada ao operador para auditoria.",
                "portaria",
                Action("Abrir Portaria", "/portaria", "meeting_room"),
                Action("Pesquisar pessoa", "/pesquisa", "person_search"))),

        new("entregas",
            ["registrar entrega", "receber encomenda", "avisar encomenda", "retirar entrega", "pacote na portaria"],
            ["entrega", "entregas", "encomenda", "encomendas", "pacote", "retirada", "receber"],
            context => Reply(
                "As entregas são registradas na Portaria. Informe a unidade, o destinatário e os dados disponíveis do pacote; na retirada, confirme quem recebeu e finalize o registro para manter o histórico correto.",
                "entregas",
                Action("Abrir Portaria", "/portaria", "meeting_room"),
                Action("Localizar destinatário", "/pesquisa", "person_search"))),

        new("operacoes",
            ["ver operacoes", "status do sistema", "fila de operacoes", "acompanhar processamento"],
            ["operacao", "operacoes", "processamento", "fila", "status", "monitorar", "monitoramento"],
            context => Reply(
                "A tela Operações reúne o acompanhamento operacional da plataforma e ajuda a identificar atividades que exigem atenção. Use os filtros para reduzir o volume e abra o item para consultar o condomínio e os detalhes relacionados.",
                "operacoes",
                Action("Abrir Operações", "/operacoes", "monitor_heart"))),

        new("pesquisa",
            ["pesquisar pessoa", "buscar morador", "consultar credencial", "achar visitante", "pesquisa global"],
            ["pesquisar", "buscar", "achar", "localizar", "morador", "pessoa", "visitante", "credencial"],
            context => Reply(
                "Use a Pesquisa para localizar rapidamente uma pessoa ou credencial em todos os condomínios aos quais você tem acesso. Pesquise pelo nome, documento ou identificador disponível e abra o resultado para seguir ao cadastro correto.",
                "pesquisa",
                Action("Ir para Pesquisa", "/pesquisa", "person_search"),
                WorkspaceAction(context, "Ver credenciais", "credenciais", "badge"))),

        new("moradores",
            ["cadastrar morador", "convidar morador", "novo morador", "adicionar pessoa", "vincular apartamento", "vincular unidade", "varias unidades"],
            ["cadastrar", "cadastro", "convidar", "convido", "convite", "morador", "moradores", "pessoa", "unidade", "unidades", "apartamento", "proprietario"],
            context => Reply(
                "Os moradores são gerenciados em Estrutura. Abra o bloco e a unidade, adicione ou vincule a pessoa e, se necessário, envie o convite de acesso. Uma mesma conta por e-mail pode participar de mais de um condomínio e de várias unidades, sem criar outro login.",
                "moradores",
                WorkspaceAction(context, "Abrir Estrutura", "estrutura", "account_tree"),
                Action("Escolher condomínio", "/licencas", "apartment"))),

        new("credenciais",
            ["criar credencial", "excluir credencial", "credencial expirada", "acesso facial", "cartao de acesso", "qr code"],
            ["credencial", "credenciais", "facial", "rosto", "biometria", "cartao", "tag", "qrcode", "qr", "expirada", "acesso"],
            context => Reply(
                "Em Credenciais você cria, consulta e encerra meios de acesso como facial, cartão ou QR, conforme os equipamentos configurados. Antes de remover uma credencial, confira a pessoa vinculada e a validade; credenciais encerradas permanecem rastreáveis no histórico.",
                "credenciais",
                WorkspaceAction(context, "Abrir Credenciais", "credenciais", "badge"),
                WorkspaceAction(context, "Ver equipamentos", "equipamentos", "sensors"))),

        new("cameras",
            ["ver cameras", "camera indisponivel", "cftv", "assistir ao vivo", "abrir portao pela camera"],
            ["camera", "cameras", "cftv", "video", "gravador", "canal", "rtsp", "imagem", "ao", "vivo"],
            context => Reply(
                "Em Câmeras, escolha o gravador e depois o canal que deseja acompanhar. A tela mostra o estado do vídeo e os comandos permitidos para seu perfil. Se um canal aparecer indisponível, confirme a conexão do equipamento e o serviço de mídia antes de alterar o cadastro.",
                "cameras",
                WorkspaceAction(context, "Abrir Câmeras", "cameras", "videocam"),
                WorkspaceAction(context, "Ver equipamentos", "equipamentos", "sensors"))),

        new("equipamentos",
            ["cadastrar equipamento", "equipamento offline", "controladora", "configurar dispositivo", "leitor facial"],
            ["equipamento", "equipamentos", "dispositivo", "offline", "controladora", "leitor", "camera", "online", "configurar"],
            context => Reply(
                "Em Equipamentos você acompanha o estado dos dispositivos, cadastra controladoras, leitores e gravadores e revisa os dados de conexão. Ao investigar um dispositivo offline, valide rede e credenciais antes de editar ou excluir o cadastro.",
                "equipamentos",
                WorkspaceAction(context, "Abrir Equipamentos", "equipamentos", "sensors"),
                WorkspaceAction(context, "Ver rotas", "rotas", "alt_route"))),

        new("rotas",
            ["criar rota de acesso", "configurar rota", "simular rota", "porta da rota", "horario de acesso"],
            ["rota", "rotas", "acesso", "porta", "portas", "horario", "simular", "trajeto"],
            context => Reply(
                "Em Rotas você define por quais dispositivos uma credencial pode passar e em quais condições. Revise portas e horários e use a simulação antes de publicar mudanças que possam bloquear acessos válidos.",
                "rotas",
                WorkspaceAction(context, "Abrir Rotas", "rotas", "alt_route"),
                WorkspaceAction(context, "Ver credenciais", "credenciais", "badge"))),

        new("relatorios",
            ["abrir relatorio", "gerar relatorio", "indicadores do condominio", "exportar dados"],
            ["relatorio", "relatorios", "indicador", "indicadores", "exportar", "dados", "grafico"],
            context => Reply(
                "Em Relatórios você acompanha indicadores consolidados do condomínio. Ajuste os filtros e o período antes de interpretar ou exportar informações, especialmente quando comparar volumes de acesso e atividades.",
                "relatorios",
                WorkspaceAction(context, "Abrir Relatórios", "relatorios", "insights"))),

        new("ocorrencias",
            ["abrir ocorrencia", "ordem de servico", "registrar problema", "manutencao preventiva", "chamar prestador"],
            ["ocorrencia", "ocorrencias", "problema", "manutencao", "ordem", "servico", "prestador", "incidente", "reparo"],
            context => Reply(
                "Em Manutenção você registra ocorrências, organiza ordens de serviço, prestadores e atividades preventivas. Informe local, prioridade e detalhes objetivos; depois acompanhe as mudanças de status até a conclusão.",
                "ocorrencias",
                WorkspaceAction(context, "Abrir Manutenção", "ocorrencias", "home_repair_service"),
                Action("Central de ocorrências", "/ocorrencias", "assignment_late"))),

        new("automacoes",
            ["criar automacao", "regra automatica", "automatizar tarefa", "desativar automacao"],
            ["automacao", "automacoes", "regra", "regras", "automatica", "automatico", "gatilho"],
            context => Reply(
                "Em Automações você cria regras para ações repetitivas. Defina um gatilho específico, confira o efeito esperado e teste com um cenário controlado antes de deixar a regra ativa.",
                "automacoes",
                WorkspaceAction(context, "Abrir Automações", "automacoes", "auto_awesome"))),

        new("emergencia",
            ["ativar emergencia", "modo emergencia", "encerrar emergencia", "alarme do condominio"],
            ["emergencia", "alarme", "critico", "evacuacao", "ativar", "encerrar"],
            context => Reply(
                "A área Emergência concentra ações críticas do condomínio. Antes de ativar, confirme o local, as instruções e o impacto; registre uma ocorrência vinculada e encerre o modo somente quando a situação estiver controlada.",
                "emergencia",
                WorkspaceAction(context, "Abrir Emergência", "emergencia", "health_and_safety"),
                WorkspaceAction(context, "Abrir Manutenção", "ocorrencias", "home_repair_service"))),

        new("reservas",
            ["reservar area", "criar reserva", "agendar espaco", "churrasqueira", "salao de festas"],
            ["reserva", "reservar", "agendamento", "agendar", "area", "comum", "espaco", "churrasqueira", "salao"],
            context => Reply(
                "No Agendamento você consulta os espaços, horários e reservas do condomínio. Para criar uma reserva, escolha o espaço e o período disponível, confira as regras e confirme os dados do responsável.",
                "reservas",
                WorkspaceAction(context, "Abrir Agendamento", "agendamento", "event_available"))),

        new("comunicados",
            ["enviar comunicado", "avisar moradores", "publicar aviso", "nova noticia"],
            ["comunicado", "comunicados", "aviso", "avisar", "publicar", "noticia", "mensagem", "moradores"],
            context => Reply(
                "Em Comunicados você prepara avisos para os moradores. Use um título direto, revise o conteúdo e o público antes de publicar. Para mensagens urgentes de segurança, use os fluxos operacionais apropriados em vez de depender apenas do comunicado.",
                "comunicados",
                WorkspaceAction(context, "Abrir Comunicados", "comunicados", "campaign"))),

        new("assembleias",
            ["criar assembleia", "abrir votacao", "registrar voto", "pauta de assembleia", "resultado da votacao"],
            ["assembleia", "assembleias", "votacao", "votar", "voto", "pauta", "quorum", "resultado"],
            context => Reply(
                "Em Assembleias você organiza pautas e acompanha votações. Revise datas, opções e regras antes de abrir a votação, pois essas informações orientam a participação e o resultado apresentado aos moradores.",
                "assembleias",
                WorkspaceAction(context, "Abrir Assembleias", "assembleias", "how_to_vote"))),

        new("financeiro",
            ["criar cobranca", "dar baixa", "conciliar pagamento", "enviar boleto", "boleto do morador"],
            ["financeiro", "cobranca", "pagamento", "baixa", "conciliacao", "boleto", "boletos", "inadimplencia", "valor"],
            context => Reply(
                "Na Gestão Financeira você acompanha cobranças, pagamentos e conciliações; em Boletos, consulta e envia os documentos vinculados às unidades. Sempre confirme competência, unidade e valor antes de emitir ou dar baixa.",
                "financeiro",
                WorkspaceAction(context, "Abrir Financeiro", "financeiro", "account_balance_wallet"),
                WorkspaceAction(context, "Ver Boletos", "boletos", "receipt_long"))),

        new("documentos",
            ["enviar documento", "arquivo do condominio", "ata da assembleia", "baixar documento"],
            ["documento", "documentos", "arquivo", "upload", "ata", "regulamento", "download"],
            context => Reply(
                "Em Documentos você centraliza arquivos do condomínio. Dê nomes claros, escolha a categoria correta e evite enviar arquivos com dados pessoais além do necessário.",
                "documentos",
                WorkspaceAction(context, "Abrir Documentos", "documentos", "description"))),

        new("administracao",
            ["adicionar usuario", "alterar permissao", "configurar condominio", "fazer backup", "restaurar backup"],
            ["administracao", "usuario", "usuarios", "permissao", "perfil", "configuracao", "backup", "restaurar", "modulo"],
            context => Reply(
                "Em Administrar ficam equipe, permissões, módulos e backups, conforme seu nível de acesso. Conceda somente as permissões necessárias e gere um backup antes de alterações operacionais importantes.",
                "administracao",
                WorkspaceAction(context, "Abrir Administração", "administracao", "admin_panel_settings"),
                Action("Segurança da conta", "/seguranca", "security"))),

        new("seguranca",
            ["trocar senha", "ativar dois fatores", "mfa", "seguranca da conta", "sessao expirada"],
            ["senha", "seguranca", "mfa", "autenticacao", "fator", "sessao", "expirada", "login", "logout", "sair"],
            context => Reply(
                "Em Segurança da conta você troca a senha e configura a autenticação em dois fatores. Se a sessão expirar, salve o que estiver preenchendo quando possível e entre novamente; o portal não deve manter acesso a áreas protegidas após a expiração.",
                "seguranca",
                Action("Abrir Segurança", "/seguranca", "security"),
                Action("Sair com segurança", "/Login/Logout", "logout"))),

        new("atualizacoes",
            ["ver atualizacoes", "novidades da plataforma", "versao da plataforma", "o que mudou"],
            ["atualizacao", "atualizacoes", "novidade", "novidades", "versao", "mudou", "release"],
            context => Reply(
                "Em Atualizações você consulta a versão exibida pelo portal e um resumo das funcionalidades e correções recentes. É o melhor lugar para entender mudanças antes de orientar a equipe.",
                "atualizacoes",
                Action("Ver Atualizações", "/atualizacoes", "new_releases"))),

        new("atalhos",
            ["mostrar atalhos", "o que voce faz", "como pode ajudar", "menu de ajuda", "onde encontro"],
            ["atalho", "atalhos", "ajuda", "ajudar", "encontrar", "onde", "menu", "funcionalidades"],
            context => Reply(
                context.IsWorkspace
                    ? "Posso explicar os recursos deste condomínio e levar você diretamente a cadastros, credenciais, câmeras, manutenção, reservas, comunicados, assembleias e financeiro. Pergunte como faria uma tarefa; não precisa saber o nome da tela."
                    : "Posso explicar como usar o portal e abrir atalhos para Portaria, Pesquisa, Condomínios, Operações, Segurança e outras áreas. Pergunte com suas palavras, por exemplo: “como convido um morador?”.",
                "atalhos",
                context.IsWorkspace
                    ? WorkspaceAction(context, "Visão geral", "visao-geral", "dashboard")
                    : Action("Ver Condomínios", "/licencas", "apartment"),
                Action("Abrir Pesquisa", "/pesquisa", "search"),
                Action("Abrir Portaria", "/portaria", "meeting_room")))
    ];

    public PortalAssistantReply Answer(string? question, string currentUri)
    {
        var context = PortalAssistantPageContext.FromUri(currentUri);
        var normalized = Normalize(question);
        if (string.IsNullOrWhiteSpace(normalized))
            return Fallback(context);

        if (IsGreeting(normalized))
        {
            return Reply(
                "Olá! Estou aqui para orientar você no uso do F&F Access. Pode descrever o que deseja fazer, como “cadastrar um morador”, “consultar uma câmera” ou “abrir uma ocorrência”.",
                "saudacao",
                Action("Ver Condomínios", "/licencas", "apartment"),
                Action("Abrir Portaria", "/portaria", "meeting_room"));
        }

        var tokens = Tokenize(normalized);
        var ranked = Intents
            .Select(intent => new { Intent = intent, Score = Score(intent, normalized, tokens, context) })
            .OrderByDescending(candidate => candidate.Score)
            .First();

        return ranked.Score >= 2 ? ranked.Intent.Reply(context) : Fallback(context);
    }

    public PortalAssistantReply Welcome(string currentUri)
    {
        var context = PortalAssistantPageContext.FromUri(currentUri);
        if (context.IsWorkspace)
        {
            var section = SectionLabel(context.Section);
            return Reply(
                $"Olá! Estou acompanhando você em {section}. Posso explicar esta área, orientar uma tarefa ou abrir um atalho dentro deste condomínio.",
                "boas-vindas",
                WorkspaceAction(context, "Visão geral", "visao-geral", "dashboard"));
        }

        var page = GlobalPageLabel(context.Path);
        return Reply(
            $"Olá! Sou o assistente de uso do F&F Access. Você está em {page}; posso explicar os recursos do portal e levar você ao caminho certo.",
            "boas-vindas",
            Action("Ver atalhos", "/licencas", "apps"));
    }

    public IReadOnlyList<PortalAssistantSuggestion> Suggestions(string currentUri)
    {
        var context = PortalAssistantPageContext.FromUri(currentUri);
        if (context.IsWorkspace)
        {
            return context.Section switch
            {
                "cameras" =>
                [
                    new("Acompanhar câmeras", "Como acompanho as câmeras ao vivo?"),
                    new("Câmera indisponível", "O que verifico quando uma câmera está indisponível?"),
                    new("Abrir ocorrência", "Como abro uma ocorrência?")
                ],
                "estrutura" =>
                [
                    new("Cadastrar morador", "Como cadastrar um morador em uma unidade?"),
                    new("Enviar convite", "Como convidar um morador?"),
                    new("Várias unidades", "Um morador pode ter várias unidades?")
                ],
                "credenciais" =>
                [
                    new("Nova credencial", "Como criar uma credencial?"),
                    new("Acesso facial", "Como funciona a credencial facial?"),
                    new("Credencial expirada", "O que fazer com uma credencial expirada?")
                ],
                "financeiro" or "boletos" =>
                [
                    new("Criar cobrança", "Como criar uma cobrança?"),
                    new("Dar baixa", "Como funciona a baixa de pagamento?"),
                    new("Ver boletos", "Onde consulto os boletos?")
                ],
                "administracao" =>
                [
                    new("Permissões", "Como alterar permissões de um usuário?"),
                    new("Backups", "Como faço um backup?"),
                    new("Módulos", "Onde configuro os módulos do condomínio?")
                ],
                _ =>
                [
                    new("Cadastrar morador", "Como cadastrar um morador?"),
                    new("Ver câmeras", "Onde vejo as câmeras?"),
                    new("Abrir ocorrência", "Como abro uma ocorrência?")
                ]
            };
        }

        return context.Path.ToLowerInvariant() switch
        {
            "/portaria" =>
            [
                new("Eventos de acesso", "Como consulto eventos de acesso?"),
                new("Abrir uma porta", "Como abrir uma porta pela portaria?"),
                new("Localizar morador", "Como localizar um morador?")
            ],
            "/pesquisa" =>
            [
                new("Buscar pessoa", "Como buscar um morador?"),
                new("Ver credenciais", "Onde vejo as credenciais de acesso?"),
                new("Escolher condomínio", "Como entro em um condomínio?")
            ],
            _ =>
            [
                new("Cadastrar morador", "Como cadastrar um morador?"),
                new("Usar a Portaria", "O que posso fazer na portaria?"),
                new("Mostrar atalhos", "Mostre os principais atalhos")
            ]
        };
    }

    private static int Score(Intent intent, string normalized, HashSet<string> tokens, PortalAssistantPageContext context)
    {
        var score = 0;
        foreach (var phrase in intent.Phrases)
        {
            var normalizedPhrase = Normalize(phrase);
            if (normalized.Contains(normalizedPhrase, StringComparison.Ordinal))
                score += normalized == normalizedPhrase ? 10 : 7;
        }

        foreach (var keyword in intent.Keywords)
        {
            if (tokens.Contains(Normalize(keyword))) score += 2;
        }

        if (context.Section == intent.Topic || context.Path.Contains(intent.Topic, StringComparison.OrdinalIgnoreCase))
            score++;

        return score;
    }

    private static PortalAssistantReply Fallback(PortalAssistantPageContext context) => Reply(
        "Ainda não tenho uma orientação segura para essa dúvida. Tente descrever a ação e o item envolvido, por exemplo “convidar morador”, “câmera indisponível” ou “dar baixa em pagamento”. Também posso abrir a Pesquisa para você.",
        "nao-encontrado",
        Action("Abrir Pesquisa", "/pesquisa", "search"),
        context.IsWorkspace
            ? WorkspaceAction(context, "Visão geral", "visao-geral", "dashboard")
            : Action("Ver Condomínios", "/licencas", "apartment"));

    private static bool IsGreeting(string normalized) =>
        normalized is "oi" or "ola" or "bom dia" or "boa tarde" or "boa noite" or "tudo bem";

    private static HashSet<string> Tokenize(string value) => TokenSeparator
        .Split(value)
        .Where(token => token.Length >= 2)
        .ToHashSet(StringComparer.Ordinal);

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(character);
        }

        return TokenSeparator.Replace(builder.ToString().Normalize(NormalizationForm.FormC), " ").Trim();
    }

    private static PortalAssistantReply Reply(string text, string topic, params PortalAssistantAction[] actions) =>
        new(text, actions.Where(action => !string.IsNullOrWhiteSpace(action.Url)).DistinctBy(action => action.Url).ToArray(), topic);

    private static PortalAssistantAction Action(string label, string url, string icon) => new(label, url, icon);

    private static PortalAssistantAction WorkspaceAction(PortalAssistantPageContext context, string label, string section, string icon) =>
        new(label, context.WorkspaceUrl(section), icon);

    private static string SectionLabel(string? section) => section switch
    {
        "visao-geral" => "Visão geral",
        "relatorios" => "Relatórios",
        "estrutura" => "Estrutura",
        "credenciais" => "Credenciais",
        "equipamentos" => "Equipamentos",
        "rotas" => "Rotas de acesso",
        "cameras" => "Câmeras",
        "ocorrencias" => "Manutenção",
        "automacoes" => "Automações",
        "emergencia" => "Emergência",
        "agendamento" => "Agendamento",
        "comunicados" => "Comunicados",
        "assembleias" => "Assembleias",
        "financeiro" => "Gestão Financeira",
        "boletos" => "Boletos",
        "documentos" => "Documentos",
        "administracao" => "Administração",
        _ => "este condomínio"
    };

    private static string GlobalPageLabel(string path) => path.ToLowerInvariant() switch
    {
        "/" or "/home/index" => "Visão geral",
        "/portaria" => "Portaria",
        "/pesquisa" => "Pesquisa",
        "/operacoes" => "Operações",
        "/licencas" => "Condomínios",
        "/ocorrencias" => "Ocorrências",
        "/cadastros" => "Cadastros",
        "/credenciais" => "Credenciais",
        "/equipamentos" => "Equipamentos",
        "/seguranca" => "Segurança da conta",
        "/atualizacoes" => "Atualizações",
        _ => "esta página"
    };
}
