// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.SemanticKernel.SkillDefinition;

/// <summary>
/// Skill collection interface.
/// </summary>
[SuppressMessage("Naming", "CA1711:Identifiers should not have incorrect suffix")]
public interface ISkillCollection : IReadOnlySkillCollection
{
    /// <summary>
    /// Add a function to the collection
    /// </summary>
    /// <param name="functionInstance">Function delegate</param>
    /// <param name="skillName">The optional skill name. If not provided, the skill name on the function instance is used. Will become required in a future release.</param>
    /// <param name="skillDescription">The optional skill description that the function instance belongs to.</param>
    /// <returns>Self instance</returns>
    ISkillCollection AddFunction(ISKFunction functionInstance, string? skillName, string? skillDescription);
}
