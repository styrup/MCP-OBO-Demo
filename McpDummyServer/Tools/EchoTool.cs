using System.ComponentModel;
using ModelContextProtocol.Server;

namespace McpDummyServer.Tools;

/// <summary>
/// The simplest possible MCP tool: echoes back whatever text you send it.
/// Use this as a template for new tools - copy the class, rename it, and
/// implement your own logic. Each [McpServerTool]-attributed method becomes
/// a separate tool that clients (like an LLM) can discover and call.
/// </summary>
[McpServerToolType]
public static class EchoTool
{
    [McpServerTool, Description("Echoes back the provided message, optionally uppercased.")]
    public static string Echo(
        [Description("The message to echo back.")] string message,
        [Description("If true, returns the message in upper case.")] bool uppercase = false)
    {
        return uppercase ? message.ToUpperInvariant() : message;
    }
}
