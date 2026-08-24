using Condotify.Models;
using CondotifyAPI.Controllers;
using CondotifyAPI.Domain.DTO.Governance;
using CondotifyAPI.Domain.Enums.Governance;
using CondotifyAPI.Domain.Enums.License;
using CondotifyAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CondotifyAPI.Tests;

public sealed class AssemblyGovernanceTests
{
    [Fact]
    public void FormValidation_ShouldAcceptCompleteSecureAssembly()
    {
        var input = ValidForm();

        Assert.Null(AssemblyRules.Validate(input));
    }

    [Theory]
    [InlineData("http://meeting.example.test", "HTTPS")]
    [InlineData("javascript:alert(1)", "HTTPS")]
    [InlineData("", "HTTPS")]
    public void VirtualAssembly_ShouldRequireHttpsMeetingUrl(string url, string expected)
    {
        var input = ValidForm();
        input.MeetingUrl = url;

        var error = AssemblyRules.Validate(input);

        Assert.NotNull(error);
        Assert.Contains(expected, error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void VotingWindow_ShouldEnforceStatusAndExactBoundaries()
    {
        var starts = new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var assembly = new CondominiumAssemblyDTO
        {
            Status = AssemblyStatusEnum.Open,
            VotingStartsAt = starts,
            VotingEndsAt = starts.AddHours(2)
        };

        Assert.NotNull(AssemblyRules.VotingError(assembly, starts.AddTicks(-1)));
        Assert.Null(AssemblyRules.VotingError(assembly, starts));
        Assert.Null(AssemblyRules.VotingError(assembly, starts.AddHours(2).AddTicks(-1)));
        Assert.NotNull(AssemblyRules.VotingError(assembly, starts.AddHours(2)));
        assembly.Status = AssemblyStatusEnum.Closed;
        Assert.NotNull(AssemblyRules.VotingError(assembly, starts.AddMinutes(1)));
    }

    [Fact]
    public void Result_ShouldUseEligibleWeightQuorumAndExcludeAbstentionFromApproval()
    {
        var item = new AssemblyAgendaItemDTO
        {
            QuorumPercentage = 60,
            ApprovalPercentage = 60,
            AbstentionCountsForQuorum = true
        };
        var yes = Option("Sim", approval: true);
        var no = Option("Não");
        var abstention = Option("Abstenção", abstention: true);
        item.Options = [yes, no, abstention];
        item.Votes =
        [
            Vote(yes.Id, 4),
            Vote(no.Id, 2),
            Vote(abstention.Id, 1)
        ];

        var result = AssemblyProjection.Calculate(item, 10);

        Assert.Equal(70, result.ParticipationPercentage);
        Assert.True(result.QuorumMet);
        Assert.True(result.ApprovalMet); // 4 de 6 votos decisivos = 66,67%
    }

    [Fact]
    public void HiddenResults_ShouldKeepResidentsOwnChoiceButHideTotals()
    {
        var residentId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var assembly = AssembliesController.CreateCore(Guid.NewGuid(), ValidForm(), "Gestor", DateTime.UtcNow);
        var item = assembly.AgendaItems.Single();
        var option = item.Options.First();
        item.Votes.Add(new AssemblyVoteDTO
        {
            Id = Guid.NewGuid(), LicenseId = assembly.LicenseId, AssemblyId = assembly.Id,
            AgendaItemId = item.Id, OptionId = option.Id, UnitId = unitId,
            ResidentId = residentId, Weight = 1, CastAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow
        });
        assembly.EligibleUnits.Add(new AssemblyEligibleUnitDTO
        {
            Id = Guid.NewGuid(), LicenseId = assembly.LicenseId, UnitId = unitId,
            Weight = 1, IsEligible = true
        });

        var output = AssemblyProjection.ToDetail(assembly, residentId, false, []);

        var agenda = Assert.Single(output.AgendaItems);
        Assert.Equal(option.Id, agenda.SelectedOptionId);
        Assert.Single(agenda.ResidentVotes);
        Assert.Empty(agenda.NamedVotes);
        Assert.All(agenda.Options, value => Assert.Equal(0, value.VoteCount));
        Assert.Equal(0, agenda.ParticipationPercentage);
    }

    [Fact]
    public void Model_ShouldPreventDuplicateVoteAndAttendancePerUnit()
    {
        using var context = CreateContext();
        var vote = context.Model.FindEntityType(typeof(AssemblyVoteDTO));
        var attendance = context.Model.FindEntityType(typeof(AssemblyAttendanceDTO));
        var eligible = context.Model.FindEntityType(typeof(AssemblyEligibleUnitDTO));

        AssertUnique(vote!, nameof(AssemblyVoteDTO.AgendaItemId), nameof(AssemblyVoteDTO.UnitId));
        AssertUnique(attendance!, nameof(AssemblyAttendanceDTO.AssemblyId), nameof(AssemblyAttendanceDTO.UnitId));
        AssertUnique(eligible!, nameof(AssemblyEligibleUnitDTO.AssemblyId), nameof(AssemblyEligibleUnitDTO.UnitId));
        Assert.NotNull(vote!.GetQueryFilter());
    }

    [Fact]
    public void PermissionAndModuleContracts_ShouldRemainBitAligned()
    {
        Assert.Equal((long)LicensePermissionEnum.ViewAssemblies, (long)LicensePermission.ViewAssemblies);
        Assert.Equal((long)LicensePermissionEnum.ManageAssemblies, (long)LicensePermission.ManageAssemblies);
        Assert.Equal((long)CondotifyAPI.Domain.Enums.License.LicenseModuleEnum.Assemblies,
            (long)Condotify.Models.LicenseModuleEnum.Assemblies);
        Assert.True(LicenseAccessDefaults.Normalize(LicensePermissionEnum.ManageAssemblies)
            .HasFlag(LicensePermissionEnum.ViewAssemblies));
    }

    private static AssemblyFormViewModel ValidForm()
    {
        var starts = DateTime.UtcNow.AddDays(7);
        return new AssemblyFormViewModel
        {
            Title = "Assembleia Geral Ordinária",
            Description = "Deliberações do condomínio.",
            Type = (int)AssemblyTypeEnum.Ordinary,
            Format = (int)AssemblyFormatEnum.Virtual,
            VoteVisibility = (int)AssemblyVoteVisibilityEnum.Secret,
            MeetingUrl = "https://meeting.example.test/assembleia",
            StartsAt = starts,
            VotingStartsAt = starts,
            VotingEndsAt = starts.AddHours(2),
            AgendaItems =
            [
                new AssemblyAgendaItemFormViewModel
                {
                    Title = "Aprovação das contas",
                    QuorumPercentage = 50,
                    ApprovalPercentage = 50
                }
            ]
        };
    }

    private static AssemblyVoteOptionDTO Option(string label, bool approval = false, bool abstention = false) => new()
    {
        Id = Guid.NewGuid(), Label = label, IsApproval = approval, IsAbstention = abstention
    };

    private static AssemblyVoteDTO Vote(Guid optionId, decimal weight) => new()
    {
        Id = Guid.NewGuid(), OptionId = optionId, Weight = weight
    };

    private static void AssertUnique(Microsoft.EntityFrameworkCore.Metadata.IEntityType entity, params string[] properties) =>
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(x => x.Name).SequenceEqual(properties));

    private static DatabaseContext CreateContext()
    {
        Environment.SetEnvironmentVariable("CONDOTIFY_EQUIPMENT_SECRET", "assembly-governance-tests-secret-2026");
        var options = new DbContextOptionsBuilder<DatabaseContext>()
            .UseNpgsql("Host=localhost;Database=Condotify_ModelOnly;Username=postgres;Password=postgres")
            .Options;
        return new DatabaseContext(options);
    }
}
