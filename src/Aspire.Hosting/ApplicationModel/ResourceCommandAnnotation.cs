// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;

namespace Aspire.Hosting.ApplicationModel;

#pragma warning disable RS0016 // Add public types and members to the declared API
/// <summary>
/// Represents a command annotation for a resource.
/// </summary>
[DebuggerDisplay("Type = {GetType().Name,nq}, Type = {Type}")]
public sealed class ResourceCommandAnnotation : IResourceAnnotation
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceCommandAnnotation"/> class.
    /// </summary>
    public ResourceCommandAnnotation(
        string type,
        string displayName,
        Func<UpdateCommandStateContext, ResourceCommandState> updateState,
        Func<ExecuteCommandContext, Task> executeCommand,
        string? iconName,
        bool isHighlighted)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(displayName);

        Type = type;
        DisplayName = displayName;
        UpdateState = updateState;
        ExecuteCommand = executeCommand;
        IconName = iconName;
        IsHighlighted = isHighlighted;
    }

    /// <summary>
    /// 
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// 
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// 
    /// </summary>
    public Func<UpdateCommandStateContext, ResourceCommandState> UpdateState { get; }

    /// <summary>
    /// 
    /// </summary>
    public Func<ExecuteCommandContext, Task> ExecuteCommand { get; }

    /// <summary>
    /// 
    /// </summary>
    public string? IconName { get; }

    /// <summary>
    /// 
    /// </summary>
    public bool IsHighlighted { get; }
}

/// <summary>
/// 
/// </summary>
public class UpdateCommandStateContext
{
    /// <summary>
    /// 
    /// </summary>
    public required CustomResourceSnapshot ResourceSnapshot { get; init; }
}

/// <summary>
/// 
/// </summary>
public class ExecuteCommandContext
{
    /// <summary>
    /// 
    /// </summary>
    public required IServiceProvider ServiceProvider { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public required string ResourceName { get; init; }

    /// <summary>
    /// 
    /// </summary>
    public required CancellationToken CancellationToken { get; init; }
}
#pragma warning restore RS0016 // Add public types and members to the declared API
