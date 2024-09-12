// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using Aspire.Hosting.Dcp;
using Microsoft.Extensions.DependencyInjection;

namespace Aspire.Hosting.ApplicationModel;

internal static class CommandsConfigurationExtensions
{
    internal const string StartType = "start";
    internal const string StopType = "stop";
    internal const string RestartType = "restart";

    internal static IResourceBuilder<T> WithLifeCycleCommands<T>(this IResourceBuilder<T> builder) where T : IResource
    {
        builder.WithCommand(
            StartType,
            "Start",
            context =>
            {
                if (IsStarting(context.ResourceSnapshot.State?.Text))
                {
                    return ResourceCommandState.Disabled;
                }
                else if (IsStopped(context.ResourceSnapshot.State?.Text))
                {
                    return ResourceCommandState.Enabled;
                }
                else
                {
                    return ResourceCommandState.Hidden;
                }
            },
            async context =>
            {
                var executor = context.ServiceProvider.GetRequiredService<ApplicationExecutor>();

                await executor.StartResourceAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
            },
            "Play",
            isHighlighted: true);

        builder.WithCommand(
            StopType,
            "Stop",
            context =>
            {
                if (IsStopping(context.ResourceSnapshot.State?.Text))
                {
                    return ResourceCommandState.Disabled;
                }
                else if (!IsStopped(context.ResourceSnapshot.State?.Text) && !IsStarting(context.ResourceSnapshot.State?.Text))
                {
                    return ResourceCommandState.Enabled;
                }
                else
                {
                    return ResourceCommandState.Hidden;
                }
            },
            async context =>
            {
                var executor = context.ServiceProvider.GetRequiredService<ApplicationExecutor>();

                await executor.StopResourceAsync(context.ResourceName, context.CancellationToken).ConfigureAwait(false);
            },
            "Stop",
            isHighlighted: true);

        builder.WithCommand(
            RestartType,
            "Restart",
            context =>
            {
                if (IsStarting(context.ResourceSnapshot.State?.Text) || IsStopping(context.ResourceSnapshot.State?.Text) || IsStopped(context.ResourceSnapshot.State?.Text))
                {
                    return ResourceCommandState.Disabled;
                }
                else
                {
                    return ResourceCommandState.Enabled;
                }
            },
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
        static bool IsStopping(string? state) => state is "Stopping";
        static bool IsStarting(string? state) => state is "Starting";
    }
}
