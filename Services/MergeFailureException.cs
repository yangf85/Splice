using System;
using Splice.Models;

namespace Splice.Services;

public sealed class MergeFailureException : Exception
{
    public MergeErrorReason Reason { get; }
    public bool StopQueue { get; }
    public int? ExitCode { get; }
    public string? Diagnostics { get; }

    public MergeFailureException(MergeErrorReason reason, string message,
        bool stopQueue = false, int? exitCode = null, string? diagnostics = null,
        Exception? innerException = null) : base(message, innerException)
    {
        Reason = reason;
        StopQueue = stopQueue;
        ExitCode = exitCode;
        Diagnostics = diagnostics;
    }
}
