using System.Security.Cryptography;

using Microsoft.Extensions.FileProviders;

namespace QuestionsHub.Blazor.Infrastructure;

/// <summary>
/// Provides a versioned URL for the icon sprite file (icons.svg).
/// Computes a content hash at startup so browsers cache-bust automatically when the file changes.
/// </summary>
public sealed class IconSpriteVersionService
{
    public string SpriteUrl { get; }

    public IconSpriteVersionService(IWebHostEnvironment env)
    {
        SpriteUrl = ComputeVersionedUrl(env.WebRootFileProvider, "/icons.svg");
    }

    private static string ComputeVersionedUrl(IFileProvider fileProvider, string path)
    {
        var fileInfo = fileProvider.GetFileInfo(path);
        if (!fileInfo.Exists || fileInfo.PhysicalPath == null)
            return path;

        using var stream = fileInfo.CreateReadStream();
        var hash = SHA256.HashData(stream);
        var version = Convert.ToHexStringLower(hash)[..12];
        return $"{path}?v={version}";
    }
}
