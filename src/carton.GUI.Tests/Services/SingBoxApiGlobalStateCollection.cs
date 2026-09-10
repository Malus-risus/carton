using Xunit;

namespace carton.GUI.Tests.Services;

/// <summary>
/// Groups every test class that points <see cref="carton.Core.Services.HttpClientFactory"/>
/// at a local API endpoint. That state is process-wide static and
/// <see cref="carton.Core.Services.SingBoxApi.SingBoxGrpcApiClient"/> re-reads it on every
/// call, so xunit must not run these classes in parallel with each other.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SingBoxApiGlobalStateCollection
{
    public const string Name = "SingBox API global state";
}
