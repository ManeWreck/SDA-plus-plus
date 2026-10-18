using System;
using System.Net;
using System.Net.Http;

namespace Steam_Desktop_Authenticator
{
    internal enum RemoteActionFailureKind
    {
        RelayUnavailable,
        AdminTimeout,
        AuthorizationExpired,
        DeviceLocked,
        RequestExpired,
        Rejected,
        Unknown
    }

    internal sealed class DesktopRegistryException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string RegistryError { get; }

        public DesktopRegistryException(HttpStatusCode statusCode, string registryError)
            : base(registryError)
        {
            StatusCode = statusCode;
            RegistryError = registryError ?? string.Empty;
        }
    }

    internal static class RemoteActionFailure
    {
        public static RemoteActionFailureKind Classify(Exception error)
        {
            if (error is OperationCanceledException) return RemoteActionFailureKind.AdminTimeout;
            if (error is HttpRequestException) return RemoteActionFailureKind.RelayUnavailable;
            if (error is DesktopRegistryException registryError)
            {
                return registryError.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => RemoteActionFailureKind.AuthorizationExpired,
                    HttpStatusCode.Forbidden => RemoteActionFailureKind.AuthorizationExpired,
                    (HttpStatusCode)423 => RemoteActionFailureKind.DeviceLocked,
                    HttpStatusCode.Conflict => RemoteActionFailureKind.RequestExpired,
                    HttpStatusCode.Gone => RemoteActionFailureKind.RequestExpired,
                    HttpStatusCode.TooManyRequests => RemoteActionFailureKind.RelayUnavailable,
                    _ => RemoteActionFailureKind.Rejected
                };
            }
            return RemoteActionFailureKind.Unknown;
        }

        public static bool CanRetryAutomatically(RemoteActionFailureKind failure)
        {
            return failure == RemoteActionFailureKind.RelayUnavailable ||
                failure == RemoteActionFailureKind.AdminTimeout ||
                failure == RemoteActionFailureKind.RequestExpired;
        }
    }
}
