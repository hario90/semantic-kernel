// Copyright (c) Microsoft. All rights reserved.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel.Diagnostics;

namespace Microsoft.SemanticKernel.SkillDefinition;

public interface ISkill
{
    /// <summary>
    /// Check if a function is available in the current context, and return it.
    /// </summary>
    /// <param name="functionName">The name of the function to retrieve.</param>
    /// <param name="availableFunction">When this method returns, the function that was retrieved if one with the specified name was found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the function was found; otherwise, <see langword="false"/>.</returns>
    bool TryGetFunction(string functionName, [NotNullWhen(true)] out ISKFunction? availableFunction);

    /// <summary>
    /// Gets the function stored in the collection.
    /// </summary>
    /// <param name="functionName">The name of the function to retrieve.</param>
    /// <returns>The function retrieved from the collection.</returns>
    /// <exception cref="SKException">The specified function could not be found in the collection.</exception>
    ISKFunction GetFunction(string functionName);

    /// <summary>
    /// Adds function to the skill collection.
    /// </summary>
    /// <param name="functionInstance"></param>
    /// <returns></returns>
    ISkill AddFunction(ISKFunction functionInstance);

    ref readonly ConcurrentDictionary<string, ISKFunction> Functions { get; }

    /// <summary>
    /// Name of the function. The name is used by the skill collection and in prompt templates e.g. {{skillName.functionName}}
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Skill description. The description is used in combination with embeddings when searching relevant skills. // TODO verify if true
    /// </summary>
    string? Description { get; }
}
