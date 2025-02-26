using System.Net;
using DotNext;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace src;

public class InvokeException : Exception
{
    public InvokeException(string message) : base(message) { }
    public InvokeException(string message, Exception innerException) : base(message, innerException) { }
}

public sealed class Empty
{
    private Empty() { }
}

public static class Invoke
{
    public static Result<T> Try<T>(Func<T> func)
    {
        try
        {
            return func();
        }
        catch (Exception e)
        {
            return Result.FromException<T>(e);
        }
    }

    public static Result<Empty> Try(Action func)
    {
        try
        {
            func();
            return new Result<Empty>();
        }
        catch (Exception e)
        {
            return Result.FromException<Empty>(e);
        }
    }

    public async static Task<Result<T>> Try<T>(Func<Task<T>> func)
    {
        try
        {
            return await func();
        }
        catch (Exception e)
        {
            return Result.FromException<T>(e);
        }
    }

    public async static Task<Result<Empty>> Try(Func<ValueTask> func)
    {
        try
        {
            await func();
            return new Result<Empty>();
        }
        catch (Exception e)
        {
            return Result.FromException<Empty>(e);
        }
    }

    public static Result<T> TryNotNull<T>(Func<T?> func)
    {
        try
        {
            var result = func();
            if (result is null)
                return Result.FromException<T>(new InvokeException("Result is null"));
            return result;
        }
        catch (Exception e)
        {
            return Result.FromException<T>(e);
        }
    }

    public async static Task<Result<T>> TryNotNull<T>(Func<Task<T?>> func)
    {
        try
        {
            var result = await func();
            if (result is null)
                return Result.FromException<T>(new InvokeException("Result is null"));
            return result;
        }
        catch (Exception e)
        {
            return Result.FromException<T>(e);
        }
    }

    public static async IAsyncEnumerable<Result<T>> TryEnumerable<T>(Func<IAsyncEnumerable<T>> func)
    {
        Result<IAsyncEnumerable<T>> result;

        try
        {
            result = Result.FromValue(func());
        }
        catch (Exception e)
        {
            result = Result.FromException<IAsyncEnumerable<T>>(new InvokeException("Failed to create IAsyncEnumerable", e));
        }

        if (!result.EnsureSuccess(out var enumerable))
        {
            yield return Result.FromException<T>(result.Error!);
            yield break;
        }

        await foreach (var item in enumerable)
        {
            yield return item;
        }
    }
}

public static class ResultExtensions
{
    public static bool EnsureSuccess<T>(this Result<T> result, out T value)
    {
        if (!result.IsSuccessful)
        {
            value = default!;
            return false;

        }

        value = result.Value;
        return true;
    }

    public static bool EnsureSuccess<T>(this Result<T> result)
    {
        return result.IsSuccessful;
    }

    public static Result<T> FromError<T>(this Result<T> result, string message)
    {
        if (!result.IsSuccessful)
            return Result.FromException<T>(new InvokeException(message, result.Error));

        return Result.FromException<T>(new InvokeException($"FromError invoked with successful result. Error message: {message}"));
    }

    public static Result<E> FromError<T, E>(this Result<T> result, string message)
    {
        if (!result.IsSuccessful)
            return Result.FromException<E>(new InvokeException(message, result.Error));

        return Result.FromException<E>(new InvokeException($"FromError invoked with successful result. Error message: {message}"));
    }

    public static void ThrowError<T>(this Result<T> result)
    {
        if (!result.IsSuccessful)
            throw new InvokeException("ThrowError invoked with unsuccessful result.", result.Error);

        throw new InvokeException("ThrowError invoked with successful result.");
    }

    public static void ThrowGuard<T>(this Result<T> result, out T value)
    {
        if (!result.IsSuccessful)
            throw new InvokeException("ThrowGuard invoked with unsuccessful result.", result.Error);

        value = result.Value;
    }

    public static async Task<Result<HttpResponseData>> HttpError<T, E>(this Result<T> result, ILogger<E> logger, HttpRequestData req, HttpStatusCode statusCode, string msg, CancellationToken ct)
    {
        if (!result.EnsureSuccess())
            logger.LogError(result.Error, "{msg}", msg);

        var responseResult = Invoke.Try(() => req.CreateResponse(statusCode));
        if (!responseResult.EnsureSuccess(out var response))
            return responseResult.FromError("Failed to create error response");

        var writeResult = await Invoke.Try(() => response.WriteAsJsonAsync(new { error = msg, }, ct));
        if (!writeResult.EnsureSuccess())
            return writeResult.FromError<Empty, HttpResponseData>("Failed to write error response");

        return response;
    }
}