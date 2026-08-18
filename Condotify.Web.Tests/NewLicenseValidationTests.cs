using System.ComponentModel.DataAnnotations;
using Condotify.Models;

namespace Condotify.Web.Tests;

public sealed class NewLicenseValidationTests
{
    [Fact]
    public void EmptyOptionalCode_DoesNotBlockSubmission()
    {
        var model = new CreateLicenseViewModel
        {
            Name = "Condominio Teste",
            CNPJ = "12345678000190",
            City = "Salvador",
            Country = "Brasil",
            Code = string.Empty
        };
        var results = new List<ValidationResult>();

        var valid = Validator.TryValidateObject(
            model,
            new ValidationContext(model),
            results,
            validateAllProperties: true);

        Assert.True(valid);
        Assert.Empty(results);
    }
}
