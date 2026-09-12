namespace SSW.TimePro.Cli.Infrastructure.ApiClient;

public class ApiException : Exception
{
    public int StatusCode { get; }
    public string? ResponseBody { get; }

    /// <summary>
    /// Correlation id for the failed attempt: the server-echoed id when it returned one,
    /// otherwise the one this client sent.
    /// </summary>
    public string? RequestId { get; }

    public ApiException(int statusCode, string message, string? responseBody = null, string? requestId = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
        RequestId = requestId;
    }
}
