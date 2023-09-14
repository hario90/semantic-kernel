// Copyright (c) Microsoft. All rights reserved.

using System;
using System.ComponentModel;
using Microsoft.SemanticKernel.SkillDefinition;

namespace Skills;

[Description("Logs text to the console")]
public sealed class ConsoleSkill
{
    [SKFunction, Description("Logs text to the console")]
    public static void Log([Description("Text to log")] string input) => Console.WriteLine(input);
}
