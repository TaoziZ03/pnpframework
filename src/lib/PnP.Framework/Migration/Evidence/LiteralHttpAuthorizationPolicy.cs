namespace PnP.Framework.Migration.Evidence
{
    public static class LiteralHttpAuthorizationPolicy
    {
        public static bool IsAuthorizationBlocked(int? statusCode)
        {
            return statusCode == 401 || statusCode == 403;
        }
    }
}
