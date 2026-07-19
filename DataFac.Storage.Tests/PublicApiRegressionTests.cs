using PublicApiGenerator;
using Shouldly;
using System.Threading.Tasks;
using VerifyXunit;
using Xunit;

#pragma warning disable CA1707 // Identifiers should not contain underscores
#pragma warning disable CA2007 // Consider calling ConfigureAwait on the awaited task

namespace DataFac.Storage.Tests;

public class PublicApiRegressionTests
{
    [Fact]
    public async Task CheckVerifySetup()
    {
        await VerifyChecks.Run();
    }

    [Fact]
    public void VersionCheck()
    {
        var assemblyVersion = typeof(BlobData).Assembly.GetName().Version;
        assemblyVersion.ShouldNotBeNull();
        assemblyVersion.ToString().ShouldBe("3.1.0.0");
    }

    [Fact]
    public async Task CheckPublicApi()
    {
        // act
        var options = new ApiGeneratorOptions()
        {
            IncludeAssemblyAttributes = false
        };
        string currentApi = ApiGenerator.GeneratePublicApi(typeof(BlobData).Assembly, options);

        // assert
        await Verifier.Verify(currentApi);
    }

}
