using System.Globalization;

namespace SecondDimensionWatcherReDive.WebDav;

public static class WebDavStatusCodes
{
    public const int Ok = 200;
    public const int Created = 201;
    public const int NoContent = 204;
    public const int MultiStatus = 207;
    public const int BadRequest = 400;
    public const int Forbidden = 403;
    public const int NotFound = 404;
    public const int MethodNotAllowed = 405;
    public const int Conflict = 409;
    public const int PreconditionFailed = 412;
    public const int UnsupportedMediaType = 415;
    public const int UnprocessableEntity = 422;
    public const int Locked = 423;
    public const int FailedDependency = 424;
    public const int InternalServerError = 500;
    public const int InsufficientStorage = 507;

    public static string FormatStatusLine(int statusCode)
    {
        var reason = GetReasonPhrase(statusCode);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"HTTP/1.1 {statusCode} {reason}");
    }

    private static string GetReasonPhrase(int statusCode) => statusCode switch
    {
        Ok => "OK",
        Created => "Created",
        NoContent => "No Content",
        MultiStatus => "Multi-Status",
        BadRequest => "Bad Request",
        Forbidden => "Forbidden",
        NotFound => "Not Found",
        MethodNotAllowed => "Method Not Allowed",
        Conflict => "Conflict",
        PreconditionFailed => "Precondition Failed",
        UnsupportedMediaType => "Unsupported Media Type",
        UnprocessableEntity => "Unprocessable Entity",
        Locked => "Locked",
        FailedDependency => "Failed Dependency",
        InternalServerError => "Internal Server Error",
        InsufficientStorage => "Insufficient Storage",
        _ => "Unknown"
    };
}
