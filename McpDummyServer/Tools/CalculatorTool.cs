using System.ComponentModel;
using ModelContextProtocol.Server;

namespace McpDummyServer.Tools;

/// <summary>
/// A slightly less trivial example than EchoTool: a couple of basic math
/// operations. Shows how a single tool type can expose multiple related
/// tools, and how to validate input and return structured errors.
/// </summary>
[McpServerToolType]
public static class CalculatorTool
{
    [McpServerTool, Description("Adds two numbers together.")]
    public static double Add(
        [Description("The first number.")] double a,
        [Description("The second number.")] double b)
        => a + b;

    [McpServerTool, Description("Divides the first number by the second number.")]
    public static double Divide(
        [Description("The numerator.")] double numerator,
        [Description("The denominator. Must not be zero.")] double denominator)
    {
        if (denominator == 0)
        {
            throw new ArgumentException("Denominator must not be zero.", nameof(denominator));
        }

        return numerator / denominator;
    }
}
