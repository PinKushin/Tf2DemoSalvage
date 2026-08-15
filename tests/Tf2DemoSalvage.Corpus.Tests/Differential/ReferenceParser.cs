using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace Tf2DemoSalvage.Core.Tests.Differential;

/// <summary>
/// Runs <c>tf-demo-parser</c>'s <c>parse_demo</c> and returns its JSON, or reports that it is
/// unavailable.
/// </summary>
/// <remarks>
/// The oracle is an external, optional dependency: a Rust binary built from
/// <see href="https://codeberg.org/demostf/parser">demostf/parser</see>. Tests that use it skip
/// when it is absent rather than failing, because most machines will not have it — but the
/// skip is reported loudly enough that a silent green is not mistaken for a comparison having
/// happened.
///
/// Point <c>TF2DEMOSALVAGE_ORACLE</c> at the binary, or leave it unset and the well-known
/// scratch path is tried.
/// </remarks>
internal static class ReferenceParser
{
    private const string PathVariable = "TF2DEMOSALVAGE_ORACLE";

    /// <summary>Locates the oracle binary, or <c>null</c> if it is not available.</summary>
    public static string? Locate()
    {
        string? configured = Environment.GetEnvironmentVariable(PathVariable);
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured;
        }

        return null;
    }

    /// <summary>Runs the oracle over a demo and parses its JSON output.</summary>
    /// <param name="oraclePath">Path to <c>parse_demo</c>.</param>
    /// <param name="demoPath">Demo to analyse.</param>
    /// <returns>The parsed JSON document.</returns>
    /// <exception cref="InvalidOperationException">The oracle failed or produced no output.</exception>
    public static JsonDocument Run(string oraclePath, string demoPath)
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = oraclePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,

            // **Without this the oracle steals the foreground.** It is a console program, so
            // Windows allocates a console window for it even though both its streams are
            // redirected and nothing will ever be shown there — and a new console window takes
            // focus. The differential suite runs it once per demo, so a full run fires a burst of
            // window activations into whatever the person at the machine was typing into.
            // Reported for real: keystrokes and clicks landing in a browser mid-run.
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add(demoPath);

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {oraclePath}.");

        string json = process.StandardOutput.ReadToEnd();
        string errors = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException(
                $"Oracle exited {process.ExitCode} for {Path.GetFileName(demoPath)}: {errors}");
        }

        return JsonDocument.Parse(json);
    }
}
