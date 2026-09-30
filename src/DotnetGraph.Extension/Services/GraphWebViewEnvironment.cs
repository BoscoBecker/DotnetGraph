using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace DotnetGraph.Extension.Services;

internal static class GraphWebViewEnvironment
{
    private static CoreWebView2Environment? _environment;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static string UserDataFolder =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DotnetGraph",
            "WebView2");

    public static async Task<CoreWebView2Environment> GetOrCreateAsync()
    {
        if (_environment is not null)
        {
            return _environment;
        }

        await Gate.WaitAsync().ConfigureAwait(true);
        try
        {
            if (_environment is not null)
            {
                return _environment;
            }

            Directory.CreateDirectory(UserDataFolder);

            _environment = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: UserDataFolder,
                options: new CoreWebView2EnvironmentOptions()).ConfigureAwait(true);

            return _environment;
        }
        finally
        {
            Gate.Release();
        }
    }
}
