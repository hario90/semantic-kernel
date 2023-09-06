// Copyright (c) Microsoft. All rights reserved.

using System.Linq;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Microsoft.SemanticKernel.Planning.Sequential;

/// <summary>
/// Contains extension methods for <see cref="SkillView"/>
/// </summary>
internal static class SkillViewExtensions
{
    /// <summary>
    /// Create a manual-friendly string for a function.
    /// </summary>
    /// <param name="skill">The function to create a manual-friendly string for.</param>
    /// <returns>A manual-friendly string for a function.</returns>
    internal static string ToManualString(this SkillView skill)
    {
        return $@"{skill.Name}:
  description: {skill.Description}";
    }

    /// <summary>
    /// Create a string for generating an embedding for a skill.
    /// </summary>
    /// <param name="skill">The function to create a string for generating an embedding for.</param>
    /// <returns>A string for generating an embedding for a skill.</returns>
    internal static string ToEmbeddingString(this SkillView skill)
    {
        return $"{skill.Name}:\n  description: {skill.Description}\n";
    }
}
