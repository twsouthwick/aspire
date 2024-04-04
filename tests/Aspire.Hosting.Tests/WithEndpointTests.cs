// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.Hosting.Tests;

public class WithEndpointTests
{
    // copied from /src/Shared/StringComparers.cs to avoid ambiguous reference since StringComparers exists internally in multiple Hosting assemblies.
    private static StringComparison EndpointAnnotationName => StringComparison.OrdinalIgnoreCase;

    [Fact]
    public void WithEndpointInvokesCallback()
    {
        using var testProgram = CreateTestProgram();
        testProgram.ServiceABuilder.WithEndpoint(3000, 1000, name: "mybinding");
        testProgram.ServiceABuilder.WithEndpoint("mybinding", endpoint =>
        {
            endpoint.Port = 2000;
        });

        var endpoint = testProgram.ServiceABuilder.Resource.Annotations.OfType<EndpointAnnotation>()
            .Where(e => string.Equals(e.Name, "mybinding", EndpointAnnotationName)).Single();
        Assert.Equal(2000, endpoint.Port);
    }

    [Fact]
    public void WithEndpointCallbackDoesNotRunIfEndpointDoesntExistAndCreateIfNotExistsIsFalse()
    {
        var executed = false;

        using var testProgram = CreateTestProgram();
        testProgram.ServiceABuilder.WithEndpoint("mybinding", endpoint =>
        {
            executed = true;
        },
        createIfNotExists: false);

        Assert.False(executed);
        Assert.True(testProgram.ServiceABuilder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out var annotations));
        Assert.DoesNotContain(annotations, e => string.Equals(e.Name, "mybinding", EndpointAnnotationName));
    }

    [Fact]
    public void WithEndpointCallbackRunsIfEndpointDoesntExistAndCreateIfNotExistsIsDefault()
    {
        var executed = false;

        using var testProgram = CreateTestProgram();
        testProgram.ServiceABuilder.WithEndpoint("mybinding", endpoint =>
        {
            executed = true;
        });

        Assert.True(executed);
        Assert.True(testProgram.ServiceABuilder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out _));
    }

    [Fact]
    public void WithEndpointCallbackRunsIfEndpointDoesntExistAndCreateIfNotExistsIsTrue()
    {
        var executed = false;

        using var testProgram = CreateTestProgram();
        testProgram.ServiceABuilder.WithEndpoint("mybinding", endpoint =>
        {
            executed = true;
        },
        createIfNotExists: true);

        Assert.True(executed);
        Assert.True(testProgram.ServiceABuilder.Resource.TryGetAnnotationsOfType<EndpointAnnotation>(out _));
    }

    [Fact]
    public void EndpointsWithTwoPortsSameNameThrows()
    {
        var ex = Assert.Throws<DistributedApplicationException>(() =>
        {
            using var testProgram = CreateTestProgram();
            testProgram.ServiceABuilder.WithHttpsEndpoint(3000, 1000, name: "mybinding");
            testProgram.ServiceABuilder.WithHttpsEndpoint(3000, 2000, name: "mybinding");
        });

        Assert.Equal("Endpoint with name 'mybinding' already exists", ex.Message);
    }

    [Fact]
    public void EndpointsWithSinglePortSameNameThrows()
    {
        var ex = Assert.Throws<DistributedApplicationException>(() =>
        {
            using var testProgram = CreateTestProgram();
            testProgram.ServiceABuilder.WithHttpsEndpoint(1000, name: "mybinding");
            testProgram.ServiceABuilder.WithHttpsEndpoint(2000, name: "mybinding");
        });

        Assert.Equal("Endpoint with name 'mybinding' already exists", ex.Message);
    }

    [Fact]
    public void CanAddEndpointsWithContainerPortAndEnv()
    {
        using var testProgram = CreateTestProgram();
        testProgram.AppBuilder.AddExecutable("foo", "foo", ".")
                              .WithHttpEndpoint(targetPort: 3001, name: "mybinding", env: "PORT");

        var app = testProgram.Build();

        var appModel = app.Services.GetRequiredService<DistributedApplicationModel>();
        var exeResources = appModel.GetExecutableResources();

        var resource = Assert.Single(exeResources);
        Assert.Equal("foo", resource.Name);
        var endpoints = resource.Annotations.OfType<EndpointAnnotation>().ToArray();
        Assert.Single(endpoints);
        Assert.Equal("mybinding", endpoints[0].Name);
        Assert.Equal(3001, endpoints[0].TargetPort);
        Assert.Equal("http", endpoints[0].UriScheme);
        Assert.Equal("PORT", endpoints[0].EnvironmentVariable);
    }

    [Fact]
    public void GettingContainerHostNameFailsIfNoContainerHostNameSet()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
            .WithEndpoint("ep", e =>
            {
                e.AllocatedEndpoint = new(e, "localhost", 8031);
            });

        var ex = Assert.Throws<InvalidOperationException>(() =>
        {
            return container.GetEndpoint("ep").ContainerHost;
        });

        Assert.Equal("The endpoint \"ep\" has no associated container host name.", ex.Message);
    }

    [Fact]
    public void WithExternalHttpEndpointsMarkExistingHttpEndpointsAsExternal()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithEndpoint(name: "ep0")
                               .WithHttpEndpoint(name: "ep1")
                               .WithHttpsEndpoint(name: "ep2")
                               .WithExternalHttpEndpoints();

        var ep0 = container.GetEndpoint("ep0");
        var ep1 = container.GetEndpoint("ep1");
        var ep2 = container.GetEndpoint("ep2");

        Assert.False(ep0.EndpointAnnotation.IsExternal);
        Assert.True(ep1.EndpointAnnotation.IsExternal);
        Assert.True(ep2.EndpointAnnotation.IsExternal);
    }

    // Existing code...

    [Fact]
    public async Task VerifyManifestWithBothDifferentPortAndTargetPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithEndpoint(name: "ep0", port: 8080, targetPort: 3000);

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "ep0": {
                  "scheme": "tcp",
                  "protocol": "tcp",
                  "transport": "tcp",
                  "port": 8080,
                  "targetPort": 3000
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestWithHttpPortWithTargetPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithHttpEndpoint(name: "h1", targetPort: 3001);

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "h1": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 3001
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestWithHttpsAndTargetPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithHttpsEndpoint(name: "h2", targetPort: 3001);

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "h2": {
                  "scheme": "https",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 3001
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestContainerWithHttpEndpointAndNoPortsAllocatesPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithHttpEndpoint(name: "h3");

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "h3": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 8000
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestContainerWithHttpsEndpointAllocatesPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithHttpsEndpoint(name: "h4");

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "h4": {
                  "scheme": "https",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 8000
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestWithHttpEndpointAndPortOnlySetsTargetPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithHttpEndpoint(name: "otlp", port: 1004);

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "otlp": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http",
                  "targetPort": 1004
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestWithTcpEndpointAndNoPortAllocatesPort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container = builder.AddContainer("app", "image")
                               .WithEndpoint(name: "custom");

        var manifest = await ManifestUtils.GetManifest(container.Resource);
        var expectedManifest =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "custom": {
                  "scheme": "tcp",
                  "protocol": "tcp",
                  "transport": "tcp",
                  "targetPort": 8000
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestProjectWithHttpEndpointDoesNotAllocatePort()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var project = builder.AddProject<TestProject>("proj")
            .WithHttpEndpoint(name: "hp")
            .WithHttpsEndpoint(name: "hps");

        var manifest = await ManifestUtils.GetManifest(project.Resource);
        var s = manifest.ToString();
        var expectedManifest =
            """
            {
              "type": "project.v0",
              "path": "projectpath",
              "env": {
                "OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EXCEPTION_LOG_ATTRIBUTES": "true",
                "OTEL_DOTNET_EXPERIMENTAL_OTLP_EMIT_EVENT_LOG_ATTRIBUTES": "true",
                "OTEL_DOTNET_EXPERIMENTAL_OTLP_RETRY": "in_memory"
              },
              "bindings": {
                "hp": {
                  "scheme": "http",
                  "protocol": "tcp",
                  "transport": "http"
                },
                "hps": {
                  "scheme": "https",
                  "protocol": "tcp",
                  "transport": "http"
                }
              }
            }
            """;

        Assert.Equal(expectedManifest, manifest.ToString());
    }

    [Fact]
    public async Task VerifyManifestPortAllocationIsGlobal()
    {
        using var builder = TestDistributedApplicationBuilder.Create();
        var container0 = builder.AddContainer("app0", "image")
                               .WithEndpoint(name: "custom");

        var container1 = builder.AddContainer("app1", "image")
                               .WithEndpoint(name: "custom");

        var manifests = await ManifestUtils.GetManifests([container0.Resource, container1.Resource]);
        var expectedManifest0 =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "custom": {
                  "scheme": "tcp",
                  "protocol": "tcp",
                  "transport": "tcp",
                  "targetPort": 8000
                }
              }
            }
            """;

        var expectedManifest1 =
            """
            {
              "type": "container.v0",
              "image": "image:latest",
              "bindings": {
                "custom": {
                  "scheme": "tcp",
                  "protocol": "tcp",
                  "transport": "tcp",
                  "targetPort": 8001
                }
              }
            }
            """;

        Assert.Equal(expectedManifest0, manifests[0].ToString());
        Assert.Equal(expectedManifest1, manifests[1].ToString());
    }

    private static TestProgram CreateTestProgram(string[]? args = null) => TestProgram.Create<WithEndpointTests>(args);

    sealed class TestProject : IProjectMetadata
    {
        public string ProjectPath => "projectpath";

        public LaunchSettings? LaunchSettings { get; } = new();
    }
}
