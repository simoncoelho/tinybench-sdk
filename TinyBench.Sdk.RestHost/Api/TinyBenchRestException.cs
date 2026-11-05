namespace TinyBench.Sdk.RestHost.Api;

public sealed class TinyBenchRestException(string code, string detail, int statusCode) : Exception(detail)
{
    public string Code { get; } = code;

    public int StatusCode { get; } = statusCode;

    public IResult ToResult()
    {
        var error = new ApiError(Code, Message);
        return StatusCode switch
        {
            StatusCodes.Status404NotFound => Results.NotFound(error),
            _ => Results.BadRequest(error)
        };
    }
}
