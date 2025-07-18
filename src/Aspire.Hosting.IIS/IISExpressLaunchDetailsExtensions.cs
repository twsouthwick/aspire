// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Aspire.Hosting;

internal static class IISExpressLaunchDetailsExtensions
{
    public static IResourceBuilder<T> WithEndpoints<T>(this IResourceBuilder<T> resource, IISExpressLaunchDetails launchProfile)
        where T : IResourceWithEndpoints, IResourceWithEnvironment
    {
        if (launchProfile is { HttpPort: { } http })
        {
            resource.WithHttpEndpoint(targetPort: http);
        }

        if (launchProfile is { SslPort: { } https })
        {
            resource.WithHttpsEndpoint(targetPort: https);
        }

        return resource;
    }

    /// <summary>
    /// Extracts a generalized <see cref="LaunchProfile"/> that contains the IIS Express launch details from the project metadata. This will also
    /// look into old-style csproj files that do not have a launchSettings.json file but have the IIS Express settings there.
    /// </summary>
    internal static IISExpressLaunchDetails GetLaunchDetails(this IProjectMetadata metadata)
    {
        var launchJsonPath = Path.Combine(Path.GetDirectoryName(metadata.ProjectPath)!, "Properties", "launchSettings.json");

        if (File.Exists(launchJsonPath))
        {
            return ParseLaunchSettingsJson(launchJsonPath);
        }
        else if (TryParseOldStyleProject(metadata.ProjectPath, out var oldStyleMetadata))
        {
            return oldStyleMetadata;
        }

        return new();
    }

    private static IISExpressLaunchDetails ParseLaunchSettingsJson(string path)
    {
        using var stream = File.OpenRead(path);
        var metadata = new IISExpressLaunchDetails();

        if (System.Text.Json.JsonSerializer.Deserialize<IISLaunchSettings>(stream) is { Settings.IISExpress: { } iisExpressSettings } settings)
        {
            if (settings.GetIISExpressProfile() is { Use64Bit: { } use64bit })
            {
                metadata = metadata with
                {
                    Use64BitIISExpress = use64bit,
                };
            }

            metadata = metadata with
            {
                SslPort = iisExpressSettings.SslPort
            };

            if (iisExpressSettings.ApplicationUrl is { })
            {
                var url = new Uri(iisExpressSettings.ApplicationUrl);

                metadata = metadata with
                {
                    HttpPort = url.Port,
                };
            }
        }

        return metadata;

    }

    private class IISLaunchSettings
    {
        public IEnumerable<KeyValuePair<string, LaunchProfile>> Profiles { get; set; } = [];

        public LaunchProfile? GetIISExpressProfile()
        {
            foreach (var profile in Profiles)
            {
                if (profile.Value.CommandName is { } cmd && cmd.Equals("IISExpress", StringComparison.OrdinalIgnoreCase))
                {
                    return profile.Value;
                }
            }

            return null;
        }

        [JsonPropertyName("iisSettings")]
        public IISSettings? Settings { get; set; }

        public class IISSettings
        {
            [JsonPropertyName("iisExpress")]
            public IISExpressDetails? IISExpress { get; set; }
        }

        public class IISExpressDetails
        {
            [JsonPropertyName("applicationUrl")]
            public string? ApplicationUrl { get; set; }

            [JsonPropertyName("sslPort")]
            public int? SslPort { get; set; }
        }
    }

    private static bool TryParseOldStyleProject(string path, [MaybeNullWhen(false)] out IISExpressLaunchDetails metadata)
    {
        XNamespace MsbuildNS = "http://schemas.microsoft.com/developer/msbuild/2003";

        // This is the GUID for the old-style web project flavor in Visual Studio.
        var webProjectGuid = new Guid("{349c5851-65df-11da-9384-00065b846f21}");

        var doc = XDocument.Load(path);

        var project = doc.Descendants(MsbuildNS + "Project").SingleOrDefault();

        if (project is not { })
        {
            metadata = null;
            return false;
        }

        var propertyGroups = project
            .Descendants(MsbuildNS + "PropertyGroup");
        var use64bitIISExpress = propertyGroups
            .Descendants(MsbuildNS + "Use64BitIISExpress")
            .FirstOrDefault() is { } b && bool.TryParse(b.Value, out var use64BitValue) ? use64BitValue : true;
        var sslPort = propertyGroups
            .Descendants(MsbuildNS + "IISExpressSSLPort")
            .FirstOrDefault() is { } s && int.TryParse(s.Value, out var sslPortValue) ? sslPortValue : default;
        var webProjectProperties = project
            .Descendants(MsbuildNS + "ProjectExtensions")
            .Descendants(MsbuildNS + "VisualStudio")
            .Descendants(MsbuildNS + "FlavorProperties")
            .FirstOrDefault(flavor => flavor.Attribute("GUID") is { } g && Guid.TryParse(g.Value, out var guid) && guid == webProjectGuid);
        var port = webProjectProperties?
            .Descendants(MsbuildNS + "DevelopmentServerPort")
            .FirstOrDefault() is { } p && int.TryParse(p.Value, out var portValue) ? portValue : default;

        metadata = new IISExpressLaunchDetails
        {
            Use64BitIISExpress = use64bitIISExpress,
            SslPort = sslPort,
            HttpPort = port,
        };
        return true;
    }
}
