using System.Text.Json;

namespace DaisysApp.Applets.AI;

/// <summary>
/// What a session tab talks to: Claude Code (ClaudeProcess, the CLI) or the built-in agent over an OpenAI-compatible
/// API (ApiAgent: Gemini, OpenAI, OpenRouter, Ollama, LM Studio…). Both speak Claude Code's stream-json: the same
/// messages come out (stream events, whole messages, tool results, permission requests, results), so the tab draws
/// them the same way whichever it is.
/// </summary>
internal interface IAgent : IDisposable
{
    bool Running { get; }

    /// <summary>A message, on the UI thread (Claude Code's stream-json).</summary>
    event Action<JsonElement>? Message;

    /// <summary>It ended: the last of what it said about why.</summary>
    event Action<string>? Exited;

    /// <summary>The model's context window, if the agent knows it (null: worked out from the model's name).</summary>
    int? ContextWindow { get; }

    void Send(string text);
    void Control(string subtype, Dictionary<string, object?>? fields);
    void Interrupt();
    void Answer(string requestId, bool allow, JsonElement input, string? reason = null);
}
