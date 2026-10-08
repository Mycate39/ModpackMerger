using System.Net;

namespace ModpackMerger.Core.Resolution;

public static class HttpClientFactory
{
    /// <summary>Modrinth exige un User-Agent identifiant l'application.</summary>
    public const string UserAgent = "ModpackMerger/1.0 (desktop app)";

    public static HttpClient Create()
    {
        var client = new HttpClient(new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
        })
        {
            Timeout = TimeSpan.FromMinutes(5),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        return client;
    }
}
