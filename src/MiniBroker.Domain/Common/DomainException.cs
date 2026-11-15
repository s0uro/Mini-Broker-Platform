namespace MiniBroker.Domain.Common;

/// <summary>
/// Thrown when a request violates a business rule (invalid amount, quantity, etc.).
/// </summary>
public sealed class DomainException(string message) : Exception(message);
