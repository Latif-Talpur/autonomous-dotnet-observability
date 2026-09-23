using System;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public static class ClientErrorMapping
    {
        public static bool IsValid(ClientErrorReport? payload) => payload != null &&
            (payload.EventId == null || CorrelationIds.IsValid(payload.EventId)) &&
            (payload.Message?.Length ?? 0) <= 2000 && (payload.StackTrace?.Length ?? 0) <= 12000 &&
            (payload.ExceptionType?.Length ?? 0) <= 256 && (payload.Url?.Length ?? 0) <= 2048 &&
            (payload.Endpoint?.Length ?? 0) <= 2048 && (payload.Browser?.Length ?? 0) <= 512 &&
            (payload.Module?.Length ?? 0) <= 128 && (payload.Screen?.Length ?? 0) <= 128 &&
            (payload.Component?.Length ?? 0) <= 128 && (payload.Device?.Length ?? 0) <= 128 &&
            (payload.ClientVersion?.Length ?? 0) <= 128 && (payload.ApplicationVersion?.Length ?? 0) <= 128;
        public static ErrorEnvelope Map(ClientErrorReport payload, string app, string environment, string? version, string correlation, string? user)
        {
            var endpoint = payload.Endpoint ?? payload.Url;
            // Strip query/fragment, which can contain identifiers or credentials.
            if (endpoint != null) endpoint = endpoint.Split('?', '#')[0];
            return new ErrorEnvelope {
                EventId = payload.EventId ?? Guid.NewGuid().ToString("N"), Layer = ErrorLayer.Angular,
                ApplicationCode = app, EnvironmentCode = environment, ApplicationVersion = version,
                ClientVersion = payload.ClientVersion ?? payload.ApplicationVersion, CorrelationId = CorrelationIds.Normalize(correlation), UserId = user,
                Message = payload.Message, ExceptionType = payload.ExceptionType, StackTrace = payload.StackTrace,
                Module = payload.Module, Screen = payload.Screen, Component = payload.Component, Endpoint = endpoint,
                Browser = payload.Browser, Device = payload.Device, HttpStatus = payload.HttpStatus,
                OccurredAtUtc = payload.OccurredAtUtc?.ToUniversalTime() ?? DateTime.UtcNow
            };
        }
    }
}
