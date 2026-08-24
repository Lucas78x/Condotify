using Condotify.Services;

namespace Condotify.Web.Tests;

public sealed class PortalAssistantKnowledgeTests
{
    private readonly PortalAssistantKnowledge _knowledge = new();

    [Fact]
    public void WorkspaceContext_ParsesKeyAndSection()
    {
        var context = PortalAssistantPageContext.FromUri(
            "https://portal.example/condominios/residencial-sol/cameras?canal=2");

        Assert.True(context.IsWorkspace);
        Assert.Equal("residencial-sol", context.WorkspaceKey);
        Assert.Equal("cameras", context.Section);
        Assert.Equal("/condominios/residencial-sol/credenciais", context.WorkspaceUrl("credenciais"));
    }

    [Theory]
    [InlineData("Como cadastrar um morador?", "moradores", "estrutura")]
    [InlineData("A câmera está indisponível", "cameras", "cameras")]
    [InlineData("Como faço a baixa de um pagamento?", "financeiro", "financeiro")]
    [InlineData("Quero abrir uma ocorrência", "ocorrencias", "ocorrencias")]
    [InlineData("O morador pode ter várias unidades?", "moradores", "estrutura")]
    [InlineData("Quero simular uma rota", "rotas", "rotas")]
    [InlineData("Onde vejo os relatórios?", "relatorios", "relatorios")]
    [InlineData("Como criar uma automação?", "automacoes", "automacoes")]
    [InlineData("Como ativar uma emergência?", "emergencia", "emergencia")]
    public void Answer_RecognizesNaturalQuestionsAndKeepsWorkspace(
        string question,
        string expectedTopic,
        string expectedSection)
    {
        var reply = _knowledge.Answer(
            question,
            "https://portal.example/condominios/residencial-sol/visao-geral");

        Assert.Equal(expectedTopic, reply.Topic);
        Assert.Contains(reply.Actions, action =>
            action.Url == $"/condominios/residencial-sol/{expectedSection}");
    }

    [Fact]
    public void Answer_HandlesAccentsAndUppercase()
    {
        var reply = _knowledge.Answer("ONDE VEJO AS CÂMERAS AO VIVO?", "https://portal.example/licencas");

        Assert.Equal("cameras", reply.Topic);
        Assert.Contains(reply.Actions, action => action.Url == "/licencas");
    }

    [Fact]
    public void Answer_UnknownQuestion_UsesSafeFallback()
    {
        var reply = _knowledge.Answer("xyz completamente desconhecido", "https://portal.example/");

        Assert.Equal("nao-encontrado", reply.Topic);
        Assert.Contains(reply.Actions, action => action.Url == "/pesquisa");
    }

    [Fact]
    public void Suggestions_AreContextualToCurrentSection()
    {
        var suggestions = _knowledge.Suggestions(
            "https://portal.example/condominios/residencial-sol/cameras");

        Assert.Contains(suggestions, suggestion => suggestion.Label == "Câmera indisponível");
        Assert.DoesNotContain(suggestions, suggestion => suggestion.Label == "Criar cobrança");
    }

    [Fact]
    public void CondominiumQuestion_UsesGlobalSelector()
    {
        var reply = _knowledge.Answer("Como trocar de condomínio?", "https://portal.example/");

        Assert.Equal("condominios", reply.Topic);
        Assert.Contains(reply.Actions, action => action.Url == "/licencas");
    }
}
