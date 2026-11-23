using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SSW.TimePro.Cli.Features.Scrum;

/// <summary>
/// Cross-platform rich-text clipboard. macOS ships a HTML→RTF pipeline via
/// <c>textutil</c> + <c>pbcopy -Prefer rtf</c> that keeps links and bold
/// when pasting into Outlook / Apple Mail / Gmail. Linux and Windows fall
/// back to plain text for now.
/// </summary>
public class ClipboardService
{
    public enum Result { RichTextCopied, PlainTextCopied, Failed }

    /// <summary>
    /// Copies rich text (HTML → RTF on macOS) with a plain-text fallback
    /// for when the rich-text pipeline is unavailable.
    /// </summary>
    public Result CopyRich(string html, string plainFallback)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CopyMac(html, plainFallback);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return CopyLinux(plainFallback);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CopyWindows(plainFallback);
        return Result.Failed;
    }

    /// <summary>
    /// Copies plain text directly, no rich-text conversion. Works for
    /// both markdown-flavored text and pure plain text.
    /// </summary>
    public Result CopyPlain(string text)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return PipeToPbcopy(text);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return CopyLinux(text);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CopyWindows(text);
        return Result.Failed;
    }

    private static Result CopyMac(string html, string plainFallback)
    {
        // Wrap the body in a full HTML document with an explicit UTF-8
        // charset declaration. Without this, textutil silently assumes
        // MacRoman and mangles every emoji / em-dash / smart quote when
        // converting to RTF.
        var document = $"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="UTF-8"></head>
            <body>{html}</body>
            </html>
            """;

        var tmpHtml = Path.GetTempFileName() + ".html";
        try
        {
            // Force UTF-8 with a BOM-free encoding and write as bytes so we
            // don't go through the platform text writer.
            File.WriteAllBytes(tmpHtml, new System.Text.UTF8Encoding(false).GetBytes(document));

            // -inputencoding UTF-8 tells textutil not to guess; -encoding UTF-8
            // ensures the intermediate text is also UTF-8.
            var script = $"textutil -convert rtf -format html -inputencoding UTF-8 -encoding UTF-8 -stdout '{tmpHtml}' | pbcopy -Prefer rtf";
            // silent discards the pipeline's output rather than redirecting it unread,
            // which could fill a pipe and block. A timeout falls back to plain text.
            var status = Process.Run("bash", ["-c", script], silent: true, timeout: TimeSpan.FromSeconds(15));
            if (status.ExitCode == 0) return Result.RichTextCopied;
            return PipeToPbcopy(plainFallback);
        }
        catch
        {
            return PipeToPbcopy(plainFallback);
        }
        finally
        {
            if (File.Exists(tmpHtml)) File.Delete(tmpHtml);
        }
    }

    private static Result PipeToPbcopy(string text)
    {
        try
        {
            var psi = new ProcessStartInfo("pbcopy")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                StandardInputEncoding = new System.Text.UTF8Encoding(false)
            };
            using var p = Process.Start(psi);
            if (p is null) return Result.Failed;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit();
            return p.ExitCode == 0 ? Result.PlainTextCopied : Result.Failed;
        }
        catch
        {
            return Result.Failed;
        }
    }

    private static Result CopyLinux(string text)
    {
        // Try wl-copy first (Wayland), then xclip.
        foreach (var (cmd, args) in new[] { ("wl-copy", ""), ("xclip", "-selection clipboard") })
        {
            try
            {
                var psi = new ProcessStartInfo(cmd, args)
                {
                    RedirectStandardInput = true,
                    UseShellExecute = false,
                    StandardInputEncoding = new System.Text.UTF8Encoding(false)
                };
                using var p = Process.Start(psi);
                if (p is null) continue;
                p.StandardInput.Write(text);
                p.StandardInput.Close();
                p.WaitForExit();
                if (p.ExitCode == 0) return Result.PlainTextCopied;
            }
            catch
            {
                // try next
            }
        }
        return Result.Failed;
    }

    private static Result CopyWindows(string text)
    {
        try
        {
            var psi = new ProcessStartInfo(
                "powershell",
                "-NoProfile -NonInteractive -Command \"Set-Clipboard -Value ([Console]::In.ReadToEnd())\"")
            {
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardInputEncoding = new System.Text.UTF8Encoding(false)
            };
            using var p = Process.Start(psi);
            if (p is null) return Result.Failed;
            p.StandardInput.Write(text);
            p.StandardInput.Close();
            p.WaitForExit();
            return p.ExitCode == 0 ? Result.PlainTextCopied : Result.Failed;
        }
        catch
        {
            return Result.Failed;
        }
    }
}
