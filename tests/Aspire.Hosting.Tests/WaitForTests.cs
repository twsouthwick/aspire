// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Components.Common.Tests;
using Aspire.Hosting.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Xunit;
using Xunit.Abstractions;

namespace Aspire.Hosting.Tests;

public class WaitForTests(ITestOutputHelper testOutputHelper)
{
    [Fact]
    [RequiresDocker]
    public async Task EnsureDependentResourceMovesIntoWaitingState()
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var dependency = builder.AddResource(new CustomResource("test"));
        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitFor(dependency);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Running state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, "Waiting", waitingStateCts.Token);

        // Now that we know we successfully entered the Waiting state, we can swap
        // the dependency into a running state which will unblock startup and
        // we can continue executing.
        await rns.PublishUpdateAsync(dependency.Resource, s => s with
        {
            State = KnownResourceStates.Running
        });

        await startTask;

        await app.StopAsync();
    }

    [Fact]
    [RequiresDocker]
    public async Task WaitForCompletionWaitsForTerminalStateOfDependencyResource()
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var dependency = builder.AddResource(new CustomResource("test"));
        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitForCompletion(dependency);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Finished state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Waiting, waitingStateCts.Token);

        // Now that we know we successfully entered the Waiting state, we can swap
        // the dependency into a running state which will unblock startup and
        // we can continue executing.
        await rns.PublishUpdateAsync(dependency.Resource, s => s with
        {
            State = KnownResourceStates.Finished,
            ExitCode = 0
        });

        // This time we want to wait for Nginx to move into a Running state to verify that
        // it successfully started after we moved the dependency resource into the Finished, but
        // we need to give it more time since we have to download the image in CI.
        var runningStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Running, runningStateCts.Token);

        await startTask;

        await app.StopAsync();
    }

    [Fact]
    [RequiresDocker]
    public async Task WaitForThrowsIfResourceMovesToTerminalStateBeforeRunning()
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var dependency = builder.AddResource(new CustomResource("test"));
        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitFor(dependency);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Finished state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(120));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, "Waiting", waitingStateCts.Token);

        // Now that we know we successfully entered the Waiting state, we can swap
        // the dependency into a running state which will unblock startup and
        // we can continue executing.
        await rns.PublishUpdateAsync(dependency.Resource, s => s with
        {
            State = KnownResourceStates.Finished,
            ExitCode = 0
        });

        // This time we want to wait for Nginx to move into a Running state to verify that
        // it successfully started after we moved the dependency resource into the Finished, but
        // we need to give it more time since we have to download the image in CI.
        var runningStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.FailedToStart, runningStateCts.Token);

        await startTask;

        await app.StopAsync();
    }

    [Fact]
    [RequiresDocker]
    public async Task EnsureDependencyResourceThatReturnsNonMatchingExitCodeResultsInDependentResourceFailingToStart()
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var dependency = builder.AddResource(new CustomResource("test"));
        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitForCompletion(dependency, exitCode: 2);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Finished state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Waiting, waitingStateCts.Token);

        // Now that we know we successfully entered the Waiting state, we can swap
        // the dependency into a finished state which will unblock startup and
        // we can continue executing.
        await rns.PublishUpdateAsync(dependency.Resource, s => s with
        {
            State = KnownResourceStates.Finished,
            ExitCode = 3 // Exit code does not match expected exit code above intentionally.
        });

        // This time we want to wait for Nginx to move into a FailedToStart state to verify that
        // it didn't start if the dependency resource didn't finish with the correct exit code.
        var runningStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.FailedToStart, runningStateCts.Token);

        await startTask;

        await app.StopAsync();
    }

    [Fact]
    [RequiresDocker]
    public async Task DependencyWithGreaterThan1ReplicaAnnotationCausesDependentResourceToFailToStart()
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var dependency = builder.AddResource(new CustomResource("test"))
                                .WithAnnotation(new ReplicaAnnotation(2));

        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitForCompletion(dependency);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Finished state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, "FailedToStart", waitingStateCts.Token);

        await startTask;

        await app.StopAsync();
    }

    [Fact]
    [RequiresDocker]
    public async Task WaitForCompletionSucceedsIfDependentResourceEntersTerminalStateWithoutAnExitCode()
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var dependency = builder.AddResource(new CustomResource("test"));

        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitForCompletion(dependency);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Finished state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Waiting, waitingStateCts.Token);

        // Now that we know we successfully entered the Waiting state, we can end the dependency
        await rns.PublishUpdateAsync(dependency.Resource, s => s with
        {
            State = KnownResourceStates.Finished
        });

        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Running, waitingStateCts.Token);

        await startTask;

        await app.StopAsync();
    }

    [InlineData(true)]
    [InlineData(false)]
    [Theory]
    [RequiresDocker]
    public async Task WaitForWaitsForHealthChecks(bool includeHealthChecks)
    {
        using var builder = TestDistributedApplicationBuilder.Create().WithTestAndResourceLogging(testOutputHelper);

        var healthService = new TaskCompletedHealthCheck("test-health");
        builder.Services.AddHealthChecks()
            .Add(new HealthCheckRegistration(healthService.Name, healthService, default, default));

        var dependency = builder.AddResource(new CustomResource("test"))
            .WithHealthCheck(healthService.Name);

        var nginx = builder.AddContainer("nginx", "mcr.microsoft.com/cbl-mariner/base/nginx", "1.22")
                           .WithReference(dependency)
                           .WaitFor(dependency, includeHealthChecks: includeHealthChecks);

        using var app = builder.Build();

        // StartAsync will currently block until the dependency resource moves
        // into a Finished state, so rather than awaiting it we'll hold onto the
        // task so we can inspect the state of the Nginx resource which should
        // be in a waiting state if everything is working correctly.
        var startupCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var startTask = app.StartAsync(startupCts.Token);

        // We don't want to wait forever for Nginx to move into a waiting state,
        // it should be super quick, but we'll allow 60 seconds just in case the
        // CI machine is chugging (also useful when collecting code coverage).
        var waitingStateCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var rns = app.Services.GetRequiredService<ResourceNotificationService>();
        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Waiting, waitingStateCts.Token);

        // Now that we know we successfully entered the Waiting state, we can start the dependency
        await rns.PublishUpdateAsync(dependency.Resource, s => s with
        {
            State = KnownResourceStates.Running
        });

        if (includeHealthChecks)
        {
            healthService.SetResult();
        }

        await rns.WaitForResourceAsync(nginx.Resource.Name, KnownResourceStates.Running, waitingStateCts.Token);

        await startTask;

        await app.StopAsync();
    }

    private sealed class CustomResource(string name) : Resource(name), IResourceWithConnectionString
    {
        public ReferenceExpression ConnectionStringExpression => ReferenceExpression.Create($"foo");
    }

    private sealed class TaskCompletedHealthCheck(string name) : TaskCompletionSource, IHealthCheck
    {
        public string Name => name;

        public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) => Task.FromResult(CheckHealth());

        private HealthCheckResult CheckHealth() => Task switch
        {
            { IsCompletedSuccessfully: true } => HealthCheckResult.Healthy(),
            { IsFaulted: true } => HealthCheckResult.Unhealthy("Error", Task.Exception),
            { IsCanceled: true } => HealthCheckResult.Unhealthy("Canceled"),
            { IsCompleted: true } => HealthCheckResult.Unhealthy("Still running"),
            _ => HealthCheckResult.Unhealthy("Unknown")
        };
    }
}
