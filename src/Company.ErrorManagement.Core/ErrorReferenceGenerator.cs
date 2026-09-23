using System;
using System.Threading;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Core
{
    public sealed class ErrorReferenceGenerator : IErrorReferenceGenerator
    {
        private static int _sequence;

        public string Generate()
        {
            var now = DateTime.UtcNow;
            var seq = Interlocked.Increment(ref _sequence) & 0xFFFFFF;
            return $"ERR-{now:yyyyMMdd}-{seq:D6}";
        }
    }
}
