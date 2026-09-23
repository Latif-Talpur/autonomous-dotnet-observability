using System;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class ErrorReferenceGenerator : IErrorReferenceGenerator
    {
        // References are opaque and unique across processes, restarts and application instances.
        public string Generate() => $"ERR-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid():N}";
    }
}
