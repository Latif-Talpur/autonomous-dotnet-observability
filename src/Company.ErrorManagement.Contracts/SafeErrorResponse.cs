namespace Company.ErrorManagement.Contracts
{
    public sealed class SafeErrorResponse
    {
        public string Type { get; set; } = "https://erp.example/errors/unexpected";
        public string Title { get; set; } = "Unable to process the request";
        public int Status { get; set; } = 500;
        public string ErrorReference { get; set; } = string.Empty;
        public string CorrelationId { get; set; } = string.Empty;
        public bool CanReportIssue { get; set; } = true;
    }
}
