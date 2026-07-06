using System.Diagnostics;
using System.Text;

namespace DeusaldLocalizerBackend;

/// <summary>Raw result of a single git invocation.</summary>
public sealed class GitResult
{
    public int    ExitCode { get; init; }
    public string StdOut   { get; init; } = string.Empty;
    public string StdErr   { get; init; } = string.Empty;
    public bool   Success  => ExitCode == 0;
}

/// <summary>Thrown when a git command that is expected to succeed returns a non-zero exit code.</summary>
public sealed class GitCommandException : Exception
{
    public int    ExitCode { get; }
    public string StdErr   { get; }

    public GitCommandException(string command, int exitCode, string stdErr)
        : base($"git {command} failed (exit {exitCode}): {stdErr}")
    {
        ExitCode = exitCode;
        StdErr   = stdErr;
    }
}

/// <summary>A single file change reported by <c>git diff --name-status</c>.</summary>
public readonly record struct GitFileChange(char Status, string Path);

/// <summary>
/// Thin wrapper over the <c>git</c> executable. Every method runs a child process in a given
/// repository working directory. All calls for one repository must be serialized by the caller
/// (see <see cref="ProjectSerializer"/>) — git is not safe to run concurrently on one work-tree.
/// </summary>
public sealed class GitService
{
    private readonly ILogger<GitService> _Logger;

    public GitService(ILogger<GitService> logger) => _Logger = logger;

    // ── Low-level runner ──────────────────────────────────────────────────────

    /// <summary>Runs git and returns the raw result without throwing on a non-zero exit.</summary>
    public async Task<GitResult> RunAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        ProcessStartInfo psi = new()
        {
            FileName               = "git",
            WorkingDirectory       = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };
        foreach (string arg in args) psi.ArgumentList.Add(arg);

        // Never let git block on an interactive credential/host prompt.
        psi.Environment["GIT_TERMINAL_PROMPT"] = "0";

        using Process process = new() { StartInfo = psi };

        StringBuilder stdOut = new();
        StringBuilder stdErr = new();
        process.OutputDataReceived += (_, e) => { if (e.Data != null) stdOut.AppendLine(e.Data); };
        process.ErrorDataReceived  += (_, e) => { if (e.Data != null) stdErr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync(ct);

        GitResult result = new()
        {
            ExitCode = process.ExitCode,
            StdOut   = stdOut.ToString().TrimEnd('\r', '\n'),
            StdErr   = stdErr.ToString().TrimEnd('\r', '\n'),
        };

        if (!result.Success)
            _Logger.LogDebug("git {Args} -> exit {Exit}: {StdErr}", string.Join(' ', args), result.ExitCode, result.StdErr);

        return result;
    }

    /// <summary>Runs git and throws <see cref="GitCommandException"/> on a non-zero exit.</summary>
    public async Task<string> RunOrThrowAsync(string workingDir, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        GitResult result = await RunAsync(workingDir, args, ct);
        if (!result.Success)
            throw new GitCommandException(string.Join(' ', args), result.ExitCode, result.StdErr);
        return result.StdOut;
    }

    // ── High-level operations ─────────────────────────────────────────────────

    public Task CloneAsync(string remoteUrl, string targetDir, CancellationToken ct = default) =>
        RunOrThrowAsync(Directory.GetCurrentDirectory(), new[] { "clone", remoteUrl, targetDir }, ct);

    public Task FetchAsync(string repoDir, CancellationToken ct = default) =>
        RunOrThrowAsync(repoDir, new[] { "fetch", "--prune", "origin" }, ct);

    /// <summary>Resolves a revision to a commit SHA (throws if it cannot be resolved).</summary>
    public Task<string> RevParseAsync(string repoDir, string rev, CancellationToken ct = default) =>
        RunOrThrowAsync(repoDir, new[] { "rev-parse", rev }, ct);

    /// <summary>Hard-resets the work-tree to a revision, discarding all local changes.</summary>
    public Task ResetHardAsync(string repoDir, string rev, CancellationToken ct = default) =>
        RunOrThrowAsync(repoDir, new[] { "reset", "--hard", rev }, ct);

    /// <summary>Removes untracked files/dirs left behind after a reset.</summary>
    public Task CleanAsync(string repoDir, CancellationToken ct = default) =>
        RunOrThrowAsync(repoDir, new[] { "clean", "-fd" }, ct);

    /// <summary>
    /// Finds the newest commit whose message contains <paramref name="token"/>. Returns the SHA,
    /// or null when no commit matches (e.g. the sync id is unknown / older than history).
    /// </summary>
    public async Task<string?> FindCommitByMessageAsync(string repoDir, string token, CancellationToken ct = default)
    {
        string sha = await RunOrThrowAsync(repoDir,
            new[] { "log", "-1", "--fixed-strings", $"--grep={token}", "--format=%H" }, ct);
        return string.IsNullOrWhiteSpace(sha) ? null : sha.Trim();
    }

    /// <summary>Returns the added/modified/deleted files between two commits.</summary>
    public async Task<IReadOnlyList<GitFileChange>> DiffNameStatusAsync(
        string repoDir, string fromSha, string toSha, CancellationToken ct = default)
    {
        string output = await RunOrThrowAsync(repoDir,
            new[] { "diff", "--name-status", "--no-renames", fromSha, toSha }, ct);

        List<GitFileChange> changes = new();
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = line.Trim();
            if (trimmed.Length < 2) continue;

            char   status = trimmed[0];
            string path   = trimmed.Substring(1).TrimStart('\t', ' ');
            if (path.Length > 0) changes.Add(new GitFileChange(status, path));
        }
        return changes;
    }

    public Task StageAllAsync(string repoDir, CancellationToken ct = default) =>
        RunOrThrowAsync(repoDir, new[] { "add", "-A" }, ct);

    /// <summary>
    /// Commits the currently staged changes with the given author and committer identity.
    /// Returns false when there was nothing staged to commit.
    /// </summary>
    public async Task<bool> CommitAsync(
        string repoDir, string message,
        string authorName, string authorEmail,
        string committerName, string committerEmail,
        CancellationToken ct = default)
    {
        string[] args =
        {
            "-c", $"user.name={committerName}",
            "-c", $"user.email={committerEmail}",
            "commit",
            "--author", $"{authorName} <{authorEmail}>",
            "-m", message,
        };

        GitResult result = await RunAsync(repoDir, args, ct);
        if (result.Success) return true;

        // "nothing to commit" is not an error for our flow — a change may be a no-op on disk.
        if (result.StdOut.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase) ||
            result.StdErr.Contains("nothing to commit", StringComparison.OrdinalIgnoreCase))
            return false;

        throw new GitCommandException(string.Join(' ', args), result.ExitCode, result.StdErr);
    }

    /// <summary>Pushes the branch. Returns the raw result so the caller can detect a rejected push.</summary>
    public Task<GitResult> PushAsync(string repoDir, string branch, CancellationToken ct = default) =>
        RunAsync(repoDir, new[] { "push", "origin", $"HEAD:{branch}" }, ct);
}
