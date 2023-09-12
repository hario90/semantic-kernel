// Copyright (c) Microsoft. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.Memory;
using Microsoft.SemanticKernel.Planning.Sequential;
using Microsoft.SemanticKernel.SkillDefinition;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace - Using NS of SKContext
namespace Microsoft.SemanticKernel.Orchestration;
#pragma warning restore IDE0130

public static class SKContextSequentialPlannerExtensions
{
    internal const string PlannerMemoryFunctionCollectionName = "Planning.SKFunctionsManual";

    internal const string PlannerMemorySkillCollectionName = "Planning.SKSkillsManual";

    internal const string PlanSKFunctionsAreRemembered = "Planning.SKFunctionsAreRemembered";

    internal const string PlanSkillsAreRemembered = "Planning.SkillsAreRemembered";

    /// <summary>
    /// Returns a string containing the manual for all available functions.
    /// </summary>
    /// <param name="context">The SKContext to get the functions manual for.</param>
    /// <param name="kernel">TODO</param>
    /// <param name="scopedSkillsSkill">Skill that filters a list of skills to the relevant skills.</param>
    /// <param name="semanticQuery">The semantic query for finding relevant registered functions</param>
    /// <param name="config">The planner skill config.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A string containing the manual for all available functions.</returns>
    public static async Task<string> GetFunctionsManual2Async(
        this SKContext context,
        IKernel kernel,
        IDictionary<string, ISKFunction> scopedSkillsSkill,
        string? semanticQuery = null,
        SequentialPlannerConfig? config = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new SequentialPlannerConfig();

        // Use configured skill provider if available, otherwise use the default SKContext skill provider.
        ISet<string> skills = config.GetAvailableSkillsAsync is null ?
            await context.GetAvailableSkillsAsync(kernel, scopedSkillsSkill, config, semanticQuery, cancellationToken).ConfigureAwait(false) :
            await config.GetAvailableSkillsAsync(config, semanticQuery, cancellationToken).ConfigureAwait(false);
        Console.Write($"Filtered skills length: ");
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.Write($"{skills.Count}");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        //Console.WriteLine($"Filtered skills: {string.Join(", ", skills)}");

        // Use configured function provider if available, otherwise use the default SKContext function provider.
        IOrderedEnumerable<FunctionView> functions = config.GetAvailableFunctionsAsync is null ?
            await context.GetAvailableFunctionsAsync(config, semanticQuery, kernel, skills, scopedSkillsSkill, cancellationToken).ConfigureAwait(false) :
            await config.GetAvailableFunctionsAsync(config, semanticQuery, cancellationToken).ConfigureAwait(false);

        return string.Join("\n\n", functions.Select(x => x.ToManualString()));
    }

    // change name to GetFunctionsManual2Async
    // if config.UseSemanticFunctionForFunctionLookup is true, invoke skill for filtering skills and then filter functions
    // else use what you have currently

    /// <summary>
    /// Returns a list of skills that are available to the user based on the semantic query and the excluded skills and functions.
    /// </summary>
    /// <param name="context">The SKContext</param>
    /// <param name="kernel">TODO</param>
    /// <param name="scopedSkillsSkill">TODO</param>
    /// <param name="config">The planner config.</param>
    /// <param name="semanticQuery">The semantic query for finding relevant registered functions</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A set of functions that are available to the user based on the semantic query and the excluded skills and functions.</returns>
    public static async Task<ISet<string>> GetAvailableSkillsAsync(
        this SKContext context,
        IKernel kernel,
        IDictionary<string, ISKFunction> scopedSkillsSkill,
        SequentialPlannerConfig config,
        string? semanticQuery = null,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        var skillViews = context.Skills.GetSkillViews();

        var availableSkills = skillViews
            .Where(s => !config.ExcludedSkills.Contains(s.Name))
            .ToList();

        List<string> result = new();
        if (config.UseSemanticFunctionForFunctionLookup && semanticQuery != null)
        {
            Console.WriteLine("Starting get skills using semantic function");
            context.Variables.Update(semanticQuery);
            var availableSkillNames = availableSkills.Select(skill => skill.Name).ToList();
            context.Variables.Set("available_skills", string.Join(",", availableSkillNames));

            var filteredSkillsResult = await kernel.RunAsync(context.Variables, scopedSkillsSkill["FilterSkills"]).ConfigureAwait(false);

            var filteredSkills = new HashSet<string>(filteredSkillsResult.Result.Trim().Split(','));
            result = availableSkills.FindAll((skill) => filteredSkills.Contains(skill.Name) || config.IncludedSkills.Contains(skill.Name)).Select(skill => skill.Name).ToList();
        }
        else if (string.IsNullOrEmpty(semanticQuery) || config.Memory is NullMemory || config.RelevancyThreshold is null)
        {
            Console.WriteLine("Not filtering skills because no memory provider is configured");
            // If no semantic query is provided, return all available skills.
            // If a Memory provider has not been registered, return all available skills.
            result = availableSkills.Select(skill => skill.Name).ToList();
        }
        else
        {
            Console.WriteLine("Starting get skills using memory.");
            result = new List<string>();

            // Remember skills in memory so that they can be searched.
            await RememberSkillsAsync(context, config.Memory, availableSkills, cancellationToken).ConfigureAwait(false);

            Stopwatch sw2 = Stopwatch.StartNew();

            // Search for skills that match the semantic query.
            var memories = config.Memory.SearchAsync(
                PlannerMemorySkillCollectionName,
                semanticQuery!,
                config.MaxRelevantFunctions, // TODO keep?
                config.RelevancyThreshold.Value,
                cancellationToken: cancellationToken);
            sw2.Stop();
            Console.WriteLine($"Search memories took {sw2.Elapsed.TotalMilliseconds}ms");
            // Add skills that were found in the search results.
            result.AddRange(await GetRelevantSkillsAsync(context, availableSkills, memories, cancellationToken).ConfigureAwait(false));

            // Add any missing skills that were included but not found in the search results.
            result.AddRange(config.IncludedSkills);
        }
        sw.Stop();
        Console.WriteLine($"Took {sw.Elapsed.TotalMilliseconds}ms total to get relevant skills");
        return new HashSet<string>(result);
    }

    /// <summary>
    /// Returns a string containing the manual for all available functions.
    /// </summary>
    /// <param name="context">The SKContext to get the functions manual for.</param>
    /// <param name="semanticQuery">The semantic query for finding relevant registered functions</param>
    /// <param name="config">The planner skill config.</param>
    /// <param name="kernel"></param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A string containing the manual for all available functions.</returns>
    public static async Task<string> GetFunctionsManualAsync(
        this SKContext context,
        string? semanticQuery = null,
        SequentialPlannerConfig? config = null,
        IKernel? kernel = null,
        CancellationToken cancellationToken = default)
    {
        config ??= new SequentialPlannerConfig();

        // Use configured function provider if available, otherwise use the default SKContext function provider.
        IOrderedEnumerable<FunctionView> functions = config.GetAvailableFunctionsAsync is null ?
            await context.GetAvailableFunctionsAsync(config, semanticQuery, kernel, cancellationToken: cancellationToken).ConfigureAwait(false) :
            await config.GetAvailableFunctionsAsync(config, semanticQuery, cancellationToken).ConfigureAwait(false);

        return string.Join("\n\n", functions.Select(x => x.ToManualString()));
    }

    /// <summary>
    /// Returns a list of functions that are available to the user based on the semantic query and the excluded skills and functions.
    /// </summary>
    /// <param name="context">The SKContext</param>
    /// <param name="config">The planner config.</param>
    /// <param name="semanticQuery">The semantic query for finding relevant registered functions</param>
    /// <param name="kernel"></param>
    /// <param name="skillsToInclude"></param>
    /// <param name="scopedSkillsSkill"></param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    /// <returns>A list of functions that are available to the user based on the semantic query and the excluded skills and functions.</returns>
    public static async Task<IOrderedEnumerable<FunctionView>> GetAvailableFunctionsAsync(
        this SKContext context,
        SequentialPlannerConfig config,
        string? semanticQuery = null,
        IKernel? kernel = null,
        ISet<string>? skillsToInclude = null,
        IDictionary<string, ISKFunction>? scopedSkillsSkill = null,
        CancellationToken cancellationToken = default)
    {
        skillsToInclude ??= new HashSet<string>();
        var functionsView = context.Skills.GetFunctionsView();

        var availableFunctions = functionsView.SemanticFunctions
            .Concat(functionsView.NativeFunctions)
            .SelectMany(x => x.Value)
            .Where(s =>
                !config.ExcludedSkills.Contains(s.SkillName) &&
                !config.ExcludedFunctions.Contains(s.Name) &&
                (skillsToInclude.Count == 0 || skillsToInclude.Contains(s.SkillName))
                )
            .ToList();

        List<FunctionView>? result = null;
        if (string.IsNullOrEmpty(semanticQuery) && config.UseSemanticFunctionForFunctionLookup && scopedSkillsSkill != null && kernel != null)
        {
            context.Variables.Set("available_functions", string.Join("\n\n", availableFunctions.Select(f => f.ToManualString())));

            var filteredFunctionsResult = await kernel.RunAsync(semanticQuery, scopedSkillsSkill["FilterFunctions"]).ConfigureAwait(false);
        }
        else if (string.IsNullOrEmpty(semanticQuery) || config.Memory is NullMemory || config.RelevancyThreshold is null)
        {
            // If no semantic query is provided, return all available functions.
            // If a Memory provider has not been registered, return all available functions.
            result = availableFunctions;
        }
        else
        {
            result = new List<FunctionView>();

            // Remember functions in memory so that they can be searched.
            await RememberFunctionsAsync(context, config.Memory, availableFunctions, cancellationToken).ConfigureAwait(false);

            // Search for functions that match the semantic query.
            var memories = config.Memory.SearchAsync(
                PlannerMemoryFunctionCollectionName,
                semanticQuery!,
                config.MaxRelevantFunctions,
                config.RelevancyThreshold.Value,
                cancellationToken: cancellationToken);

            // Add functions that were found in the search results.
            result.AddRange(await GetRelevantFunctionsAsync(context, availableFunctions, memories, cancellationToken).ConfigureAwait(false));

            // Add any missing functions that were included but not found in the search results.
            var missingFunctions = config.IncludedFunctions
                .Except(result.Select(x => x.Name))
                .Join(availableFunctions, f => f, af => af.Name, (_, af) => af);

            result.AddRange(missingFunctions);
        }

        return result
            .OrderBy(x => x.SkillName)
            .ThenBy(x => x.Name);
    }

    private static async Task<IEnumerable<FunctionView>> GetRelevantFunctionsAsync(
        SKContext context,
        IEnumerable<FunctionView> availableFunctions,
        IAsyncEnumerable<MemoryQueryResult> memories,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        ILogger? logger = null;
        var relevantFunctions = new ConcurrentBag<FunctionView>();
        await foreach (var memoryEntry in memories.WithCancellation(cancellationToken))
        {
            var function = availableFunctions.FirstOrDefault(x => x.ToFullyQualifiedName() == memoryEntry.Metadata.Id);
            if (function != null)
            {
                logger ??= context.LoggerFactory.CreateLogger(nameof(SKContext));
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Found relevant function. Relevance Score: {0}, Function: {1}", memoryEntry.Relevance, function.ToFullyQualifiedName());
                }

                relevantFunctions.Add(function);
            }
        }

        sw.Stop();
        Console.WriteLine($"Get relevant functions took: {sw.Elapsed.TotalMilliseconds}ms");
        return relevantFunctions;
    }

    /// <summary>
    /// Saves all available functions to memory.
    /// </summary>
    /// <param name="context">The SKContext to save the functions to.</param>
    /// <param name="memory">The memory provided to store the functions to.</param>
    /// <param name="availableFunctions">The available functions to save.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    internal static async Task RememberFunctionsAsync(
        SKContext context,
        ISemanticTextMemory memory,
        List<FunctionView> availableFunctions,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        // Check if the functions have already been saved to memory.
        if (context.Variables.ContainsKey(PlanSKFunctionsAreRemembered))
        {
            sw.Stop();
            return;
        }

        foreach (var function in availableFunctions)
        {
            var functionName = function.ToFullyQualifiedName();
            var key = functionName;
            var description = string.IsNullOrEmpty(function.Description) ? functionName : function.Description;
            var textToEmbed = function.ToEmbeddingString();

            // It'd be nice if there were a saveIfNotExists method on the memory interface
            var memoryEntry = await memory.GetAsync(collection: PlannerMemoryFunctionCollectionName, key: key, withEmbedding: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (memoryEntry == null)
            {
                // TODO It'd be nice if the minRelevanceScore could be a parameter for each item that was saved to memory
                // As folks may want to tune their functions to be more or less relevant.
                // Memory now supports these such strategies.
                await memory.SaveInformationAsync(collection: PlannerMemoryFunctionCollectionName, text: textToEmbed, id: key, description: description,
                    additionalMetadata: string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        // Set a flag to indicate that the functions have been saved to memory.
        context.Variables.Set(PlanSKFunctionsAreRemembered, "true");
        sw.Stop();
        Console.WriteLine($"Remember functions took: {sw.Elapsed.TotalMilliseconds}ms");
    }

    // TODO - this was copied and is very similar to the above. see if a generic version of this fn can be written to be shared.
    /// <summary>
    /// Saves all available functions to memory.
    /// </summary>
    /// <param name="context">The SKContext to save the functions to.</param>
    /// <param name="memory">The memory provided to store the functions to.</param>
    /// <param name="availableSkills">The available functions to save.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    internal static async Task RememberSkillsAsync(
        SKContext context,
        ISemanticTextMemory memory,
        List<SkillView> availableSkills,
        CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        // Check if the functions have already been saved to memory.
        if (context.Variables.ContainsKey(PlanSkillsAreRemembered))
        {
            sw.Stop();
            return;
        }

        foreach (var skill in availableSkills)
        {
            var skillName = skill.Name;
            var key = skillName;
            var description = string.IsNullOrEmpty(skill.Description) ? skillName : skill.Description;
            var textToEmbed = skill.ToEmbeddingString();

            // It'd be nice if there were a saveIfNotExists method on the memory interface
            var memoryEntry = await memory.GetAsync(collection: PlannerMemorySkillCollectionName, key: key, withEmbedding: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (memoryEntry == null)
            {
                // TODO It'd be nice if the minRelevanceScore could be a parameter for each item that was saved to memory
                // As folks may want to tune their functions to be more or less relevant.
                // Memory now supports these such strategies.
                await memory.SaveInformationAsync(collection: PlannerMemorySkillCollectionName, text: textToEmbed, id: key, description: description,
                    additionalMetadata: string.Empty, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        // Set a flag to indicate that the functions have been saved to memory.
        context.Variables.Set(PlanSkillsAreRemembered, "true");
        sw.Stop();
        Console.WriteLine($"Remember skills took: {sw.Elapsed.TotalMilliseconds}ms");
    }

    // TODO Reorder methods and consider making a generic method for this to be shared for getting skills/functions
    private static async Task<IEnumerable<string>> GetRelevantSkillsAsync(
    SKContext context,
    IEnumerable<SkillView> availableSkills,
    IAsyncEnumerable<MemoryQueryResult> memories,
    CancellationToken cancellationToken = default)
    {
        Stopwatch sw = Stopwatch.StartNew();
        ILogger? logger = null;
        var relevantSkills = new ConcurrentBag<string>();
        await foreach (var memoryEntry in memories.WithCancellation(cancellationToken))
        {
            var skill = availableSkills.FirstOrDefault(x => x.Name == memoryEntry.Metadata.Id);
            if (skill != null)
            {
                logger ??= context.LoggerFactory.CreateLogger(nameof(SKContext));
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Found relevant function. Relevance Score: {0}, Function: {1}", memoryEntry.Relevance, skill.Name);
                }

                relevantSkills.Add(skill.Name);
            }
        }

        sw.Stop();
        Console.WriteLine($"GetRelevantSkillsAsync took: {sw.Elapsed.TotalMilliseconds}ms");
        return relevantSkills;
    }
}
