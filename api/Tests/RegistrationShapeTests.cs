using System.Text.Json;
using Xunit;

namespace Croesus.Api.Tests;

/// <summary>
/// Reads the captured Microsoft Entra application-registration exports (<c>assets/*.txt</c>) and asserts
/// structural facts about them. These tests do not run the API; they pin what the exported registrations
/// actually contain so the evidence narrative cannot silently drift from the captured artifacts:
/// <list type="number">
/// <item>Each export is a PUBLIC single-page-application client — it has SPA redirect URIs but no web
/// redirect URIs, no key/password credentials, no exposed API scopes, and no app roles. Without a
/// confidential-client credential the On-Behalf-Of exchange is structurally impossible for these
/// registrations.</item>
/// <item>The SPA redirect URIs include paths ending in <c>.aspx</c> or containing
/// <c>affwebservices</c>. Path names alone do not classify the browser UI or establish a registration
/// mismatch.</item>
/// </list>
/// </summary>
public sealed class RegistrationShapeTests
{
    public static IEnumerable<object[]> ExportFiles()
    {
        yield return new object[] { "dev-dev.txt" };
        yield return new object[] { "dev-prod.txt" };
        yield return new object[] { "prod-prod.txt" };
    }

    [Theory]
    [MemberData(nameof(ExportFiles))]
    public void Export_IsPublicSpaClient_SoOboIsStructurallyImpossible(string fileName)
    {
        var root = LoadExport(fileName);

        // A public SPA client: SPA redirect URIs are present...
        Assert.True(ArrayLength(root, "spa", "redirectUris") > 0,
            $"{fileName}: expected spa.redirectUris to be populated for a SPA client.");

        // ...but none of the confidential-client / resource-server surfaces are populated. Any of these being
        // non-empty would contradict the "public SPA client, OBO structurally impossible" characterization.
        Assert.Equal(0, ArrayLength(root, "web", "redirectUris"));
        Assert.Equal(0, ArrayLength(root, "keyCredentials"));
        Assert.Equal(0, ArrayLength(root, "passwordCredentials"));
        Assert.Equal(0, ArrayLength(root, "api", "oauth2PermissionScopes"));
        Assert.Equal(0, ArrayLength(root, "appRoles"));
    }

    [Theory]
    [MemberData(nameof(ExportFiles))]
    public void SpaRedirectUris_ContainAspxOrAffWebServicesPaths(string fileName)
    {
        var root = LoadExport(fileName);

        var matchingRedirects = EnumerateStrings(root, "spa", "redirectUris")
            .Where(uri => uri.EndsWith(".aspx", StringComparison.OrdinalIgnoreCase)
                          || uri.Contains("affwebservices", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // This pins only the literal redirect-path evidence. Rendering topology and registration correctness
        // depend on runtime behavior, especially which component redeems the authorization code.
        Assert.NotEmpty(matchingRedirects);
    }

    private static JsonElement LoadExport(string fileName)
    {
        var path = Path.Combine(ResolveAssetsDirectory(), fileName);
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        // Clone so the element remains valid after the JsonDocument is disposed.
        return document.RootElement.Clone();
    }

    /// <summary>
    /// Walks up from the test binary's base directory until a folder containing <c>assets/dev-dev.txt</c> is
    /// found, so the tests do not depend on a fixed relative depth between bin output and the repo root.
    /// </summary>
    private static string ResolveAssetsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "assets");
            if (File.Exists(Path.Combine(candidate, "dev-dev.txt")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the 'assets' directory (containing dev-dev.txt) by walking up from " +
            AppContext.BaseDirectory + ".");
    }

    /// <summary>Returns the length of the array at the given property path, treating a missing/non-array value as empty (0).</summary>
    private static int ArrayLength(JsonElement root, params string[] path)
        => TryGetArray(root, path, out var array) ? array.GetArrayLength() : 0;

    /// <summary>Yields each string element of the array at the given property path; yields nothing when the path is absent.</summary>
    private static IEnumerable<string> EnumerateStrings(JsonElement root, params string[] path)
    {
        if (!TryGetArray(root, path, out var array))
        {
            yield break;
        }

        foreach (var element in array.EnumerateArray())
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString();
                if (value is not null)
                {
                    yield return value;
                }
            }
        }
    }

    private static bool TryGetArray(JsonElement root, string[] path, out JsonElement array)
    {
        var current = root;
        foreach (var segment in path)
        {
            if (current.ValueKind != JsonValueKind.Object
                || !current.TryGetProperty(segment, out var next))
            {
                array = default;
                return false;
            }

            current = next;
        }

        if (current.ValueKind == JsonValueKind.Array)
        {
            array = current;
            return true;
        }

        array = default;
        return false;
    }
}
