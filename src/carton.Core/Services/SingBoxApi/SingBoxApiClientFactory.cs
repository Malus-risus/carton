namespace carton.Core.Services.SingBoxApi;

internal static class SingBoxApiClientFactory
{
    public static ISingBoxApiClient Create(Action<string>? log = null)
    {
        return new ClashHttpApiClient(log);
    }
}
