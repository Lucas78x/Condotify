using CondotifyAPI.Domain.Interfaces;

namespace CondotifyAPI.Domain.Services;

// Uma instancia por requisicao (Scoped, registrada em Program.cs).
// Populada uma vez, no inicio do pipeline, por TenantScopeActionFilter
// (Task 5) -- nunca antes disso.
public sealed class CurrentTenantAccessor : ICurrentTenantAccessor
{
    public HashSet<Guid>? AccessibleLicenseIds { get; private set; }
    public Guid? AccessibleEnterpriseId { get; private set; }

    public void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId)
    {
        AccessibleLicenseIds = licenseIds;
        AccessibleEnterpriseId = enterpriseId;
    }
}

// Usado como valor padrao nos construtores de DatabaseContext que nao
// passam por injecao de dependencia (migrations, DatabaseContextFactory,
// DatabaseModelTests). AccessibleLicenseIds sempre null -> o filtro
// esconde tudo (fail-closed). Nenhum desses caminhos executa uma consulta
// de verdade contra uma entidade filtrada, entao isso nunca e observado
// na pratica -- e so a rede de seguranca para o caso de alguem um dia
// executar uma consulta por esse caminho.
public sealed class NullCurrentTenantAccessor : ICurrentTenantAccessor
{
    public static readonly NullCurrentTenantAccessor Instance = new();
    public HashSet<Guid>? AccessibleLicenseIds => null;
    public Guid? AccessibleEnterpriseId => null;

    public void SetAccessibleScope(HashSet<Guid> licenseIds, Guid? enterpriseId) =>
        throw new InvalidOperationException(
            "NullCurrentTenantAccessor e somente leitura (usado fora do pipeline de requisicao HTTP).");
}
