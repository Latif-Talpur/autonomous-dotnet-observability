using System;
using System.Collections.Generic;

namespace Company.ErrorManagement.Contracts
{
    public sealed class TicketCreateRequest
    {
        public string ErrorReference { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string ReportedBy { get; set; } = string.Empty;
        public string Source { get; set; } = "USER";
        public int Priority { get; set; } = 3;
        public string? QueueCode { get; set; }
        public string? SeverityCode { get; set; }
    }

    public sealed class TicketView
    {
        public string TicketId { get; set; } = string.Empty;
        public string TicketNumber { get; set; } = string.Empty;
        public string StatusCode { get; set; } = string.Empty;
        public string StatusName { get; set; } = string.Empty;
        public string? QueueCode { get; set; }
        public string? SeverityCode { get; set; }
        public int Priority { get; set; }
        public string? AssignedTo { get; set; }
        public string? Description { get; set; }
        public string ReportedBy { get; set; } = string.Empty;
        public DateTime OpenedAtUtc { get; set; }
        public DateTime? ResolvedAtUtc { get; set; }
        public DateTime? ClosedAtUtc { get; set; }
        public string? ResolutionCode { get; set; }
        public string? ResolutionNotes { get; set; }
        public string ErrorReference { get; set; } = string.Empty;
        public string ErrorDefinitionId { get; set; } = string.Empty;
    }

    public sealed class TicketAssignmentRequest
    {
        public string AssignedTo { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public sealed class TicketStatusChangeRequest
    {
        public string NewStatusCode { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public sealed class TicketCommentRequest
    {
        public string CommentText { get; set; } = string.Empty;
        public string CreatedBy { get; set; } = string.Empty;
        public string Visibility { get; set; } = "INTERNAL";
    }

    public sealed class TicketResolutionRequest
    {
        public string ResolutionCode { get; set; } = string.Empty;
        public string? ResolutionNotes { get; set; }
        public string ChangedBy { get; set; } = string.Empty;
    }

    public sealed class TicketStatusHistoryEntry
    {
        public string HistoryId { get; set; } = string.Empty;
        public string? PreviousStatusCode { get; set; }
        public string NewStatusCode { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public DateTime ChangedAtUtc { get; set; }
        public string? Reason { get; set; }
    }

    public sealed class TicketSearchQuery
    {
        public string? QueueCode { get; set; }
        public string? StatusCode { get; set; }
        public string? AssignedTo { get; set; }
        public string? SeverityCode { get; set; }
        public string? Text { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; } = 25;
    }

    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
        public int TotalCount { get; set; }
        public int PageIndex { get; set; }
        public int PageSize { get; set; }
    }
}
