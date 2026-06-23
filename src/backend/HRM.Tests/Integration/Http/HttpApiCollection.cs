namespace HRM.Tests.Integration.Http;

/// <summary>
/// xUnit collection that shares a single <see cref="ApiTestFactory"/> (one Postgres Testcontainer +
/// one ASP.NET Core app instance) across every HTTP integration test class. Without this, each class's
/// <c>IClassFixture&lt;ApiTestFactory&gt;</c> would spin up its own container + app and xUnit runs test
/// classes in parallel, exhausting memory on small hosts. Tests in this collection therefore run
/// sequentially against one shared DB, so they rely on unique (timestamp/guid) keys rather than empty
/// tables.
/// </summary>
[CollectionDefinition("HttpApi")]
public sealed class HttpApiCollection : ICollectionFixture<ApiTestFactory>
{
}
