namespace CondotifyAPI.Domain.Interfaces;

// Marca uma entidade como pertencendo a uma unica licenca (condominio).
// DatabaseContext.OnModelCreating aplica HasQueryFilter a toda entidade
// que implementa esta interface, via reflexao -- ver Task 2. Nao adicione
// esta interface a uma entidade sem entender essa consequencia: a partir
// do momento que implementa, toda consulta e restrita ao conjunto de
// licencas acessiveis da requisicao atual.
public interface ILicenseScoped
{
    Guid LicenseId { get; }
}
