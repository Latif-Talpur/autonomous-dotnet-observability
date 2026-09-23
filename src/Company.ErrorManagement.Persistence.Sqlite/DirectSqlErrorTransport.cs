using System.Threading;
using System.Threading.Tasks;
using Company.ErrorManagement.Contracts;

namespace Company.ErrorManagement.Persistence.Sqlite
{
    public sealed class DirectSqlErrorTransport : IErrorTransport
    {
        private readonly IErrorRepository _repository;

        public DirectSqlErrorTransport(IErrorRepository repository)
        {
            _repository = repository;
        }

        public Task<ErrorReceipt> SendAsync(ErrorEnvelope error, CancellationToken cancellationToken)
        {
            return _repository.UpsertAsync(error, cancellationToken);
        }
    }
}
