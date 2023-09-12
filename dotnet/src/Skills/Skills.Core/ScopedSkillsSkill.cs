// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.SemanticKernel.Orchestration;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Microsoft.SemanticKernel.Skills.Core;

// TODO better name, docs everywhere
public sealed class ScopedSkillsSkill
{
    public ScopedSkillsSkill(
        IKernel kernel,
        int? maxTokens = null,
        string? selectSkillsPrompt = null,
        char? skillDelimiter = null)
    {
        string selectSkillsPromptTemplate = selectSkillsPrompt ??this._selectSkillsPrompt;
        this._selectSkillsFunction = kernel.CreateSemanticFunction(
           promptTemplate: selectSkillsPromptTemplate,
           skillName: RestrictedSkillNameForSkillSelection,
           description: "TODO",
           maxTokens: maxTokens ?? MaxTokens,
           temperature: 0.0);
        this.skillDelimiter = skillDelimiter ?? DefaultSkillDelimiter;
        this._selectFunctionsFunction = kernel.CreateSemanticFunction(
           promptTemplate: selectSkillsPromptTemplate,
           skillName: RestrictedSkillNameForSkillSelection,
           description: "TODO",
           maxTokens: maxTokens ?? MaxTokens,
           temperature: 0.0);
    }

    [SKFunction, Description("Filters a list of skills based on a given goal")]
    public async Task<SKContext> FilterSkills(
         [Description("filter")] string input,
        SKContext context,
        CancellationToken cancellationToken = default)
    {
        var filteredSkillsResult = await this._selectSkillsFunction.InvokeAsync(context, cancellationToken: cancellationToken).ConfigureAwait(false);
        // TODO need to run semantic function multiple times to chunk the request. check out ConversationSummarySkill for how to do this
        context.Variables.Update(filteredSkillsResult.Result.Trim());
        return context;
    }

    // TODO comment, underscore necessary?
    private readonly ISKFunction _selectSkillsFunction;
    private readonly ISKFunction _selectFunctionsFunction;
    private readonly string _selectSkillsPrompt = @"
Select the skills that are relevant to the goal given.
[AVAILABLE SKILLS]

{{$available_skills}}

[END AVAILABLE SKILLS]

Output only the skill names on one line, delimited by commas.
Begin!

<goal>{{$input}}</goal>
";
    private readonly string _selectFunctionsPrompt = @"
Select the functions that are relevant to the goal given.
[AVAILABLE FUNCTIONS]

{{$available_functions}}

[END AVAILABLE FUNCTIONS]

Output only the skill names on one line, delimited by commas.
Begin!

<goal>{{$input}}</goal>
";
    // TODO comment, name
    private const string RestrictedSkillNameForSkillSelection = "SequentialPlanner_SkillSelection_Excluded";
    /// <summary>
    /// The max tokens to process in a single semantic function call.
    /// </summary>
    private const int MaxTokens = 1024;
    private const char DefaultSkillDelimiter = ',';
    private readonly char skillDelimiter;
}
