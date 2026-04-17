using Xunit;

namespace CurlUnity.IntegrationTests.Fixtures
{
    [CollectionDefinition("Integration")]
    public class IntegrationTestCollection
        : ICollectionFixture<CurlGlobalFixture>,
          ICollectionFixture<TestServerFixture>
    {
    }
}
