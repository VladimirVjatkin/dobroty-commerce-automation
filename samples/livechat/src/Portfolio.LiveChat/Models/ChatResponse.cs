namespace Portfolio.LiveChat.Models;

/// <summary>
/// The response envelope used for every application-generated outcome (success and every
/// mapped failure). Serializes as <c>{ "reply": "..." }</c> under the framework's default
/// camelCase JSON naming policy.
/// </summary>
public sealed record ChatResponse(string Reply);
