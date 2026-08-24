using System;
using System.IO;

namespace Tf2DemoSalvage.Logging;

/// <summary>
/// Keeps the newest few files of one kind in a folder, and deletes the rest.
/// </summary>
/// <remarks>
/// **It lives in the logging project rather than in `Scene`, and that is placement rather than
/// meaning (D83).** Its two consumers are the log writer and the viewer's screenshots, and this is
/// the leaf project both can reference — `Scene` cannot hold it once `Scene` depends on the logger.
/// Nothing about it is logging-specific.
///
/// **One function rather than one per writer, because the viewer had two writers and only one of
/// them pruned.** Measured 2026-08-19 on the owner's machine: 233 screenshots at 203 MB with no
/// retention code at all, and 207 run logs against a stated limit of 50. Nothing reported either;
/// disk simply went. A fix applied separately in two places is two things that can drift, and
/// these two had already drifted as far as drift goes — one of them did not exist.
///
/// **Pruned by count rather than by age**, because a quiet week should not throw away the last
/// thing measured. Which files survive is decided by ORDINAL NAME ORDER, which both writers make
/// chronological by stamping the name — <c>viewer-yyyyMMdd-HHmmss-pid.log</c> and
/// <c>shot-yyyyMMdd-HHmmss.png</c>. Sorting by name rather than by timestamp is deliberate: a
/// file's mtime is whatever the filesystem last recorded, and copying a folder rewrites all of
/// them, while the name is what the run itself said.
/// </remarks>
public static class FileRetention
{
    /// <summary>Deletes all but the newest <paramref name="keep"/> files matching a pattern.</summary>
    /// <param name="folder">The folder to prune.</param>
    /// <param name="pattern">A wildcard the files to consider must match, e.g. <c>viewer-*.log</c>.</param>
    /// <param name="keep">How many of the newest to leave behind.</param>
    /// <remarks>
    /// **Call this AFTER writing the new file, never before.** That ordering is the whole fix for
    /// the growth this was written to stop: a UI suite or a mutation run starts many viewers at
    /// once, and pruning first means every process computes its deletions from a snapshot none of
    /// its siblings has written into yet. Each trims to the limit, then each adds one, and the
    /// folder settles at the limit plus however many raced. Pruning afterwards means the last
    /// writer to finish sees the full set and trims it, so the count converges whatever the
    /// interleaving.
    ///
    /// **By the caller's own naming, never a wildcard over the whole folder.** The logs and the
    /// F12 captures live in the same directory, so a sweep of "old files" would take the
    /// screenshots somebody pressed a key to keep — the same mistake as pruning a shared
    /// measurement directory by a name glob and deleting a neighbour's run.
    ///
    /// Failures are swallowed, deliberately. Retention is tidiness: an undeletable file is a
    /// housekeeping problem, and a viewer that refused to start over one would have traded the
    /// job for the cleaning. A file that vanished between the listing and the delete is the
    /// ordinary concurrent case and is the same non-event.
    /// </remarks>
    public static void Keep(string folder, string pattern, int keep)
    {
        ArgumentNullException.ThrowIfNull(folder);
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentOutOfRangeException.ThrowIfNegative(keep);

        try
        {
            string[] present = Directory.GetFiles(folder, pattern);

            // At or under the limit there is nothing to do. `<=` rather than `<`: at exactly the
            // limit the old code fell through and deleted one, leaving keep-1.
            if (present.Length <= keep)
            {
                return;
            }

            Array.Sort(present, StringComparer.Ordinal);

            // Delete exactly the excess: indices 0 .. (count - keep - 1). The old loop ran to
            // `<= count - keep`, one past the end of the excess, so it always took one file more
            // than it should have.
            int excess = present.Length - keep;
            for (int index = 0; index < excess; index++)
            {
                try
                {
                    File.Delete(present[index]);
                }
                catch (Exception failure) when (
                    failure is IOException or UnauthorizedAccessException)
                {
                    // One file being locked or already gone must not stop the rest being cleaned.
                    // Under concurrency this is expected rather than exceptional: two writers can
                    // both decide to remove the same file, and the loser lands here.
                }
            }
        }
        catch (Exception failure) when (
            failure is IOException or UnauthorizedAccessException or ArgumentException or
                DirectoryNotFoundException)
        {
            // The folder is missing or unreadable. Nothing to prune, and nothing worth failing a
            // launch over.
        }
    }
}
