// Copyright (c) Microsoft. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Diagnostics;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.Planning.Sequential;
using Microsoft.SemanticKernel.SkillDefinition;

#pragma warning disable IDE0130
// ReSharper disable once CheckNamespace - Using NS of Plan
namespace Microsoft.SemanticKernel.Planning;
#pragma warning restore IDE0130

/// <summary>
/// A planner that uses semantic function to create a sequential plan.
/// </summary>
public sealed class SequentialPlanner : ISequentialPlanner
{
    private const string StopSequence = "<!-- END -->";

    /// <summary>
    /// Initialize a new instance of the <see cref="SequentialPlanner"/> class.
    /// </summary>
    /// <param name="kernel">The semantic kernel instance.</param>
    /// <param name="config">The planner configuration.</param>
    /// <param name="prompt">Optional prompt override</param>
    /// <param name="selectSkillsPrompt">Optional skill filter prompt override</param>
    public SequentialPlanner(
        IKernel kernel,
        SequentialPlannerConfig? config = null,
        string? prompt = null,
        string? selectSkillsPrompt = null)
    {
        Verify.NotNull(kernel);
        this.Config = config ?? new();

        this.Config.ExcludedSkills.Add(RestrictedSkillName);

        string promptTemplate = prompt ?? EmbeddedResource.Read("skprompt.txt");

        this._functionFlowFunction = kernel.CreateSemanticFunction(
            promptTemplate: promptTemplate,
            skillName: RestrictedSkillName,
            description: "Given a request or command or goal generate a step by step plan to " +
                         "fulfill the request using functions. This ability is also known as decision making and function flow",
            maxTokens: this.Config.MaxTokens ?? 1024,
            temperature: 0.0,
            stopSequences: new[] { StopSequence });

        string selectSkillsPromptTemplate = selectSkillsPrompt ?? EmbeddedResource.Read("skprompt.txt");
        this._selectSkillsFunction = kernel.CreateSemanticFunction(
            promptTemplate: selectSkillsPromptTemplate,
            skillName: RestrictedSkillNameForSkillSelection,
            description: "TODO",
            maxTokens: this.Config.MaxTokens ?? 1024,
            temperature: 0.0,
            stopSequences: new[] { StopSequence });

        this._context = kernel.CreateNewContext();
    }

    /// <inheritdoc />
    public async Task<Plan> CreatePlanAsync(string goal, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(goal))
        {
            throw new SKException("The goal specified is empty");
        }

        string relevantFunctionsManual = await this._context.GetFunctionsManualAsync(goal, this.Config, cancellationToken).ConfigureAwait(false);
        this._context.Variables.Set("available_functions", relevantFunctionsManual);

        this._context.Variables.Update(goal);

        var planResult = await this._functionFlowFunction.InvokeAsync(this._context, cancellationToken: cancellationToken).ConfigureAwait(false);
;
        if (planResult.ErrorOccurred)
        {
            throw new SKException($"Error creating plan for goal: {planResult.LastException?.Message}", planResult.LastException);
        }

        //string planResultString = "<plan>\n <!-- Summarize information about John Doe -->\n <function.SummarizeSkill.Summarize input=\"John Doe is a mysterious man with a hidden past.\"/>\n\n <!-- Generate a short poem about John Doe -->\n <function.WriterSkill.ShortPoem input=\"John Doe is a man of mystery,\n His past is shrouded in history.\"/>\n\n <!-- Translate the poem into Italian -->\n <function.WriterSkill.Translate input=\"$RESULT__SHORTPOEM_OUTPUT\" language=\"Italian\" appendToResult=\"RESULT__TRANSLATED_POEM\"/>\n\n</plan>\n";
        string planResultString = planResult.Result.Trim();

        var getSkillFunction = this.Config.GetSkillFunction ?? SequentialPlanParser.GetSkillFunction(this._context);
        var plan = planResultString.ToPlanFromXml(goal, getSkillFunction, this.Config.AllowMissingFunctions);

        if (plan.Steps.Count == 0)
        {
            throw new SKException($"Not possible to create plan for goal with available functions.\nGoal:{goal}\nFunctions:\n{relevantFunctionsManual}");
        }

        return plan;
    }

    /// <inheritdoc />
    public async Task<string?> CreatePlan2Async(string goal, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(goal))
        {
            throw new SKException("The goal specified is empty");
        }

        string relevantFunctionsManual = await this._context.GetFunctionsManualAsync(goal, this.Config, cancellationToken).ConfigureAwait(false);
        this._context.Variables.Set("available_functions", relevantFunctionsManual);
        string relevantSkillsManual = await this._context.GetSkillsManualAsync(goal, this.Config, cancellationToken).ConfigureAwait(false);
        this._context.Variables.Set("available_skills", relevantSkillsManual);

        this._context.Variables.Update(goal);

        var filteredSkillsResult = await this._selectSkillsFunction.InvokeAsync(this._context, cancellationToken: cancellationToken).ConfigureAwait(false);

        //var planResult = await this._functionFlowFunction.InvokeAsync(this._context, cancellationToken: cancellationToken).ConfigureAwait(false);

        //if (planResult.ErrorOccurred)
        //{
        //    throw new SKException($"Error creating plan for goal: {planResult.LastException?.Message}", planResult.LastException);
        //}

        ////string planResultString = "<plan>\n <!-- Summarize information about John Doe -->\n <function.SummarizeSkill.Summarize input=\"John Doe is a mysterious man with a hidden past.\"/>\n\n <!-- Generate a short poem about John Doe -->\n <function.WriterSkill.ShortPoem input=\"John Doe is a man of mystery,\n His past is shrouded in history.\"/>\n\n <!-- Translate the poem into Italian -->\n <function.WriterSkill.Translate input=\"$RESULT__SHORTPOEM_OUTPUT\" language=\"Italian\" appendToResult=\"RESULT__TRANSLATED_POEM\"/>\n\n</plan>\n";
        //string planResultString = planResult.Result.Trim();

        //var getSkillFunction = this.Config.GetSkillFunction ?? SequentialPlanParser.GetSkillFunction(this._context);
        //var plan = planResultString.ToPlanFromXml(goal, getSkillFunction, this.Config.AllowMissingFunctions);

        //if (plan.Steps.Count == 0)
        //{
        //    throw new SKException($"Not possible to create plan for goal with available functions.\nGoal:{goal}\nFunctions:\n{relevantFunctionsManual}");
        //}

        return filteredSkillsResult.Result?.Trim();
    }

    private SequentialPlannerConfig Config { get; }

    private readonly SKContext _context;

    /// <summary>
    /// the function flow semantic function, which takes a goal and creates an xml plan that can be executed
    /// </summary>
    private readonly ISKFunction _functionFlowFunction;

    // TODO comment
    private readonly ISKFunction _selectSkillsFunction;

    /// <summary>
    /// The name to use when creating semantic functions that are restricted from plan creation
    /// </summary>
    private const string RestrictedSkillName = "SequentialPlanner_Excluded";

    // TODO comment
    private const string RestrictedSkillNameForSkillSelection = "SequentialPlanner_SkillSelection_Excluded";
}
