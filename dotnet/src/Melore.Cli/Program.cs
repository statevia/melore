using System.CommandLine;
using Melore.Cli;

return await new MeloreCli(Console.Out).BuildRoot().Parse(args).InvokeAsync();
