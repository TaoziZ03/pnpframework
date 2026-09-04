using System;
using System.Net;
using System.Reflection;

namespace PnP.Framework.Migration.Topology.Ingredients
{
    internal static class TopologyHttpStatusExtractor
    {
        public static bool TryGetLiteralStatus(Exception exception, out int statusCode)
        {
            for (var current = exception; current != null; current = current.InnerException)
            {
                if (current is WebException webException && webException.Response is HttpWebResponse response)
                {
                    statusCode = (int)response.StatusCode;
                    return true;
                }
                var statusProperty = current.GetType().GetProperty("StatusCode", BindingFlags.Instance | BindingFlags.Public);
                if (statusProperty != null)
                {
                    var value = statusProperty.GetValue(current);
                    if (value is HttpStatusCode httpStatus)
                    {
                        statusCode = (int)httpStatus;
                        return true;
                    }
                    if (value is int integerStatus)
                    {
                        statusCode = integerStatus;
                        return true;
                    }
                }
                if (current.Data != null && current.Data.Contains("HttpStatusCode"))
                {
                    var value = current.Data["HttpStatusCode"];
                    if (value is int integerStatus)
                    {
                        statusCode = integerStatus;
                        return true;
                    }
                    if (value is HttpStatusCode httpStatus)
                    {
                        statusCode = (int)httpStatus;
                        return true;
                    }
                }
            }
            statusCode = 0;
            return false;
        }
    }
}
