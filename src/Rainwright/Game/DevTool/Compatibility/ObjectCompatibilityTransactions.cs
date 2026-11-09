using System;
using System.Runtime.ExceptionServices;

namespace DryCycle.DevUI.DevTool.Compatibility;

// Only orchestrates the existing authoring snapshot/restore boundary. It owns no codec,
// business data, or persistence. Shared by read-only UI construction and explicit edits.
internal static class ObjectCompatibilityTransactions
{
    internal static TResult Read<TState, TResult>(Func<TState> capture, Func<TState, string> fingerprint,
        Func<TState, bool> restore, Func<TResult> read, Action<string, Exception> report) where TState : class
    {
        TState before = capture() ?? throw new InvalidOperationException("Cannot protect author data before reading original object UI.");
        TResult result = default;
        Exception failure = null;
        try { result = read(); }
        catch (Exception error) { failure = error; }
        bool changed = false;
        try
        {
            var after = capture() ?? throw new InvalidOperationException("Cannot verify author data after reading original object UI.");
            changed = !string.Equals(fingerprint(before), fingerprint(after), StringComparison.Ordinal);
        }
        catch (Exception verification)
        {
            changed = true;
            failure = failure == null ? verification : new AggregateException("Original UI read and verification failed.", failure, verification);
        }
        if (changed)
        {
            try
            {
                RestoreAndVerify(before, capture, fingerprint, restore);
                report?.Invoke("Original object UI changed author data during a read; original data restored and verified.", null);
            }
            catch (Exception recovery)
            {
                report?.Invoke("Original object UI read recovery failed.", recovery);
                if (failure != null) throw new AggregateException("Original UI read failed and author data recovery failed.", failure, recovery);
                throw;
            }
        }
        if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        return result;
    }

    internal static bool Edit<TState>(Func<TState> capture, Func<TState, string> fingerprint,
        Func<TState, bool> restore, Func<bool> edit, Action<string, Exception> report,
        out TState before, out TState after) where TState : class
    {
        before = null; after = null;
        bool succeeded = false;
        try
        {
            before = capture() ?? throw new InvalidOperationException("Cannot protect author data before an original object edit.");
            succeeded = edit();
            after = capture() ?? throw new InvalidOperationException("Cannot verify author data after an original object edit.");
            if (succeeded) return true;
        }
        catch (Exception error) { report?.Invoke("Original object edit failed.", error); }
        if (before == null) return false;
        try
        {
            // A rejected callback may have changed data before returning false or throwing.
            // Refresh-only/popup-only actions do not require a data restore.
            if (after == null || !string.Equals(fingerprint(before), fingerprint(after), StringComparison.Ordinal))
            {
                RestoreAndVerify(before, capture, fingerprint, restore);
                report?.Invoke("Failed original object edit rolled back; author data recovery verified.", null);
            }
            after = capture();
        }
        catch (Exception recovery)
        {
            report?.Invoke("Failed original object edit could not fully restore author data.", recovery);
            // Preserve a recoverable Undo entry if the original mod refuses complete rollback.
            try { after = capture(); }
            catch (Exception error) { after = null; report?.Invoke("Residual author data snapshot failed.", error); }
        }
        return false;
    }

    private static void RestoreAndVerify<TState>(TState before, Func<TState> capture,
        Func<TState, string> fingerprint, Func<TState, bool> restore) where TState : class
    {
        if (!restore(before)) throw new InvalidOperationException("Original author data restore returned false.");
        var verified = capture();
        if (verified == null || !string.Equals(fingerprint(before), fingerprint(verified), StringComparison.Ordinal))
            throw new InvalidOperationException("Original author data restore did not reproduce the original state.");
    }
}
