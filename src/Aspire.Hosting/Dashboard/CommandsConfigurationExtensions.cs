// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dcp;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.Dashboard;

internal static class CommandsConfigurationExtensions
{
    internal static IResourceBuilder<T> WithLifeCycleCommands<T>(this IResourceBuilder<T> builder) where T : IResource
    {
        builder.WithCommand(
            "start",
            "Start",
            context => IsStopped(context.ResourceSnapshot.State?.Text) ? ResourceCommandState.Enabled : ResourceCommandState.Hidden,
            async context =>
            {
                var executor = context.ServiceProvider.GetRequiredService<ApplicationExecutor>();

                await executor.StartResourceAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
            },
            "Play",
            isHighlighted: true);

        builder.WithCommand(
            "stop",
            "Stop",
            context => !IsStopped(context.ResourceSnapshot.State?.Text) ? ResourceCommandState.Enabled : ResourceCommandState.Hidden,
            async context =>
            {
                var executor = context.ServiceProvider.GetRequiredService<ApplicationExecutor>();

                await executor.StopResourceAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
            },
            "Stop",
            isHighlighted: true);

        builder.WithCommand(
            "restart",
            "Restart",
            context => !IsStopped(context.ResourceSnapshot.State?.Text) ? ResourceCommandState.Enabled : ResourceCommandState.Hidden,
            async context =>
            {
                var executor = context.ServiceProvider.GetRequiredService<ApplicationExecutor>();

                await executor.StopResourceAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
                await executor.StartResourceAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
            },
            "ArrowCounterclockwise",
            isHighlighted: false);

        return builder;

        static bool IsStopped(string? state) => state is "Exited" or "Finished" or "FailedToStart";
    }
}
