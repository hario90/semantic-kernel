// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.SemanticKernel.SkillDefinition;

/// <summary>
/// A collection of Semantic Kernel prompt functions. // todo confirm don't need to implement disposable
/// </summary>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
internal sealed class Skill : ISkill
{
    /// <summary>
    /// TODO
    /// </summary>
    /// <param name="name"></param>
    /// <param name="description"></param>
    /// <param name="loggerFactory"></param>
    public Skill(string name, string? description, ILoggerFactory? loggerFactory = null)
    {
        Verify.NotNull(name);
        this.Name = name;
        this.Description = description;
        this._logger = loggerFactory is not null ? loggerFactory.CreateLogger(nameof(SkillCollection)) : NullLogger.Instance;

        // Important: names are case insensitive
        this._functionCollection = new(StringComparer.OrdinalIgnoreCase);
    }

    public ref readonly ConcurrentDictionary<string, ISKFunction> Functions => ref this._functionCollection;

    /// <summary>
    /// Name of the function. The name is used by the skill collection and in prompt templates e.g. {{skillName.functionName}}
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Skill description. The description is used in combination with embeddings when searching relevant skills. // TODO verify if true
    /// </summary>
    public string? Description { get; }

    //// <inheritdoc/>
    public bool TryGetFunction(string functionName, [NotNullWhen(true)] out ISKFunction? availableFunction)
    {
        Verify.NotNull(functionName);

        return this._functionCollection.TryGetValue(functionName, out availableFunction);
    }

    //// <inheritdoc/>
    public ISKFunction GetFunction(string functionName)
    {
        if (!this.TryGetFunction(functionName, out ISKFunction? functionInstance))
        {
            this.ThrowFunctionNotAvailable(this.Name, functionName);
        }

        return functionInstance;
    }

    /// <summary>
    /// Adds function to the skill collection.
    /// </summary>
    /// <param name="functionInstance"></param>
    /// <returns><see cref="ISkill"/></returns>
    /// <inheritdoc/>
    public ISkill AddFunction(ISKFunction functionInstance)
    {
        Verify.NotNull(functionInstance);

        this._functionCollection.GetOrAdd(functionInstance.Name, functionInstance);
        return this;
    }

    // TODO debugger display?
    #region private ================================================================================
    [DoesNotReturn]
    private void ThrowFunctionNotAvailable(string skillName, string functionName)
    {
        this._logger.LogError("Function not available: skill:{0} function:{1}", skillName, functionName);
        throw new SKException($"Function not available {skillName}.{functionName}");
    }

    private readonly ILogger _logger;
    private readonly ConcurrentDictionary<string, ISKFunction> _functionCollection;
    #endregion
}
