using Microsoft.AspNetCore.Http;

namespace March7thHoney.WebServer.Http;

/// <summary>Responses in the <see cref="StatusResult" /> envelope (<c>retcode</c>, <c>message</c>, <c>data: null</c>).</summary>
internal static class WebResults
{
    /// <summary>A success that carries only a message.</summary>
    public static IResult Ok(string message)
    {
        return Results.Json(new StatusResult(0, message));
    }

    /// <summary>A failure with an HTTP error status, for pages that check the status code (admin page, account recovery).</summary>
    public static IResult Error(string message, int retcode = WebConstants.RetcodeFailed,
        int statusCode = StatusCodes.Status400BadRequest)
    {
        return Results.Json(new StatusResult(retcode, message), statusCode: statusCode);
    }

    /// <summary>A failure answered with 200 OK: the SDK login and registration pages read only the retcode.</summary>
    public static IResult SdkError(string message, int retcode = WebConstants.RetcodeFailed)
    {
        return Results.Json(new StatusResult(retcode, message));
    }
}
