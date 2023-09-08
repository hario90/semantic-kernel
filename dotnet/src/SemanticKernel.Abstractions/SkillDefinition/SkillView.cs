// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;

namespace Microsoft.SemanticKernel.SkillDefinition;

/// <summary>
/// TODO. + Rename to PluginView?
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class SkillView
{
    /// <summary>
    /// Name of the function. The name is used by the skill collection and in prompt templates e.g. {{skillName.functionName}}
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Function description. The description is used in combination with embeddings when searching relevant functions.
    /// </summary>
    public string? Description { get; set; } = string.Empty;

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string DebuggerDisplay => string.IsNullOrEmpty(this.Description)
       ? this.Name
       : $"{this.Name} ({this.Description})";
}
