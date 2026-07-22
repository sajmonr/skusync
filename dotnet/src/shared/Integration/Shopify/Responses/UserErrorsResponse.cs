namespace Integration.Shopify.Responses;

/// <summary>
/// Represents an error response containing details about user-related issues during an operation.
/// </summary>
/// <remarks>
/// This class is primarily used to encapsulate error information such as the message describing the error
/// and the associated field causing the error, if any.
/// It is typically utilized in responses where user errors need to be reported.
/// </remarks>
public record UserErrorsResponse(string Message, string Field);