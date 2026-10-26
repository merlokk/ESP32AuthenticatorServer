namespace AuthServer.Shared;

/// <summary>
/// An expected failure. <see cref="Code"/> is stable and maps to a Problem Details <c>type</c>,
/// which is public API; <see cref="Message"/> is text and may change.
/// </summary>
public sealed record Error(string Code, string Message);
