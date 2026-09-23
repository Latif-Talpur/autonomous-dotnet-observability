using System;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Transport.Http
{
    internal sealed class SpoolEntry
    {
        public DateTimeOffset SpooledAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public ErrorEnvelope Envelope { get; set; } = new ErrorEnvelope();
    }
}
