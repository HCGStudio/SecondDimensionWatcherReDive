using System.Collections;

namespace SecondDimensionWatcherReDive.Models;

public static class ResponseDataExtension
{
    public static ResponseData<T> ToResponseData<T>(this T data) where T : IList
    {
        return new ResponseData<T>(data, data.Count);
    }

    public static ResponseData<T> ToResponseData<T>(this T data, int count)
    {
        return new ResponseData<T>(data, count);
    }
}