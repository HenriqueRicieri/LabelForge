using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace LabelForge.App.Services;

/// <summary>An update that is available, with what the user needs to read about it.
/// <see cref="Handle"/> is the source's own description, opaque to the UI.</summary>
public sealed record AppUpdate(string Version, object Handle);

/// <summary>
/// Where updates come from. An interface so the harness can stand in a source that
/// answers without the network: checking a real feed from a test run would be a
/// request to a third party, the same rule as Labelary.
/// </summary>
public interface IAppUpdates
{
    /// <summary>True when this copy was installed by Setup; a build folder or a
    /// portable copy has nothing to update in place.</summary>
    bool IsInstalled { get; }

    /// <summary>The version this copy reports, for the About box and the messages.</summary>
    string CurrentVersion { get; }

    /// <returns>The newer version, or null when this one is the latest.</returns>
    Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default);

    Task DownloadAsync(AppUpdate update, Action<int>? progress, CancellationToken cancellationToken = default);

    /// <summary>Applies a downloaded update and restarts the app; does not return.</summary>
    void ApplyAndRestart(AppUpdate update);
}

/// <summary>
/// Updates from the public repository's GitHub Releases through Velopack, which is what
/// built the installer. Stable releases only: a pre-release is not offered.
/// </summary>
public sealed class VelopackAppUpdates : IAppUpdates
{
    public const string RepositoryUrl = "https://github.com/HenriqueRicieri/LabelForge";

    private readonly Lazy<UpdateManager?> _manager = new(() =>
    {
        try
        {
            return new UpdateManager(new GithubSource(RepositoryUrl, null, false));
        }
        catch (Exception)
        {
            // Velopack could not locate an installation; treated as not installed.
            return null;
        }
    });

    public bool IsInstalled => _manager.Value?.IsInstalled == true;

    public string CurrentVersion => _manager.Value is { IsInstalled: true, CurrentVersion: { } installed }
        ? installed.ToString()
        : AppVersion.Display;

    public async Task<AppUpdate?> CheckAsync(CancellationToken cancellationToken = default)
    {
        if (_manager.Value is not { IsInstalled: true } manager)
        {
            return null;
        }

        UpdateInfo? info = await manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        return info is null ? null : new AppUpdate(info.TargetFullRelease.Version.ToString(), info);
    }

    public Task DownloadAsync(AppUpdate update, Action<int>? progress, CancellationToken cancellationToken = default) =>
        _manager.Value is { } manager && update.Handle is UpdateInfo info
            ? manager.DownloadUpdatesAsync(info, progress, cancellationToken)
            : Task.CompletedTask;

    public void ApplyAndRestart(AppUpdate update)
    {
        if (_manager.Value is { } manager && update.Handle is UpdateInfo info)
        {
            manager.ApplyUpdatesAndRestart(info.TargetFullRelease);
        }
    }
}

/// <summary>The version this build reports: the project version, plus the short commit
/// the SDK stamps into the informational version when the build ran in the repository.</summary>
public static class AppVersion
{
    public static string Display
    {
        get
        {
            string? informational = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(informational))
            {
                return typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "unknown";
            }

            int plus = informational.IndexOf('+', StringComparison.Ordinal);
            if (plus < 0)
            {
                return informational;
            }

            string commit = informational[(plus + 1)..];
            return $"{informational[..plus]} ({(commit.Length > 7 ? commit[..7] : commit)})";
        }
    }
}
