// Copyright (c) 2026 speakertorockets
// SPDX-License-Identifier: MIT

// helpbuilder <markdown folder> <output folder>
// Converts the user guide to HTML. Broken links are reported in MSBuild error format and give exit code 1.
using HelpBuilder;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: helpbuilder <markdown folder> <output folder>");
    return 2;
}

IReadOnlyList<GuideProblem> problems = GuideBuilder.Build(args[0], args[1]);
foreach (GuideProblem p in problems)
    Console.Error.WriteLine($"{p.File}({p.Line},{p.Column}): error HELP001: {p.Message}");
return problems.Count == 0 ? 0 : 1;
