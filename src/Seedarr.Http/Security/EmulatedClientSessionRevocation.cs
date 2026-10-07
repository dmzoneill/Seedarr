using Microsoft.AspNetCore.Http;

namespace Seedarr.Http.Security;

public static class EmulatedClientSessionRevocation
{
    public static readonly string[] SessionCookieNames =
    {
        "SID",
        "_session_id",
        "deluge-session",
    };

    public static void RevokeAll(IRpcSessionStore sessionStore, HttpResponse response)
    {
        sessionStore?.InvalidateAll();
        DeleteSessionCookies(response);
    }

    public static void DeleteSessionCookies(HttpResponse response)
    {
        if (response == null)
        {
            return;
        }

        foreach (var cookieName in SessionCookieNames)
        {
            response.Cookies.Delete(cookieName);
        }
    }
}
