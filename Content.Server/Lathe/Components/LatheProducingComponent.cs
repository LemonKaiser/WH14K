using Content.Shared.Lathe;

namespace Content.Server.Lathe.Components;

/// <summary>
/// For EntityQuery to keep track of which lathes are producing
/// </summary>
[RegisterComponent]
public sealed partial class LatheProducingComponent : Component
{
    /// <summary>
    /// The time at which production began
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan StartTime;

    /// <summary>
    /// How long it takes to produce the recipe.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan ProductionLength;

    /// <summary>
    /// The queue batch that is currently being printed.
    /// </summary>
    [ViewVariables]
    public LatheRecipeBatch? ActiveBatch;

    /// <summary>
    /// Queue index of <see cref="ActiveBatch"/> when production started.
    /// Used to restore the batch if production is aborted before completion.
    /// </summary>
    [ViewVariables]
    public int ActiveBatchIndex = -1;
}

