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

    public Result Copy(string html, string plainFallback)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return CopyMac(html, plainFallback);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return CopyLinux(plainFallback);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return CopyWindows(plainFallback);
        return Result.Failed;
    }

    private static Result CopyMac(string html, string plainFallback)
    {
        var tmpHtml = Path.GetTempFileName() + ".html";
        try
        {
            File.WriteAllText(tmpHtml, html);
            var script = $"cat '{tmpHtml}' | textutil -stdin -format html -convert rtf -stdout | pbcopy -Prefer rtf";
            var psi = new ProcessStartInfo("bash", $"-c \"{script}\"")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p is null) return PipeToPbcopy(plainFallback);
            p.WaitForExit();
            if (p.ExitCode == 0) return Result.RichTextCopied;
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
                UseShellExecute = false
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
                    UseShellExecute = false
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
            var psi = new ProcessStartInfo("powershell", $"-NoProfile -Command \"Set-Clipboard -Value @'\n{text}\n'@\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p is null) return Result.Failed;
            p.WaitForExit();
            return p.ExitCode == 0 ? Result.PlainTextCopied : Result.Failed;
        }
        catch
        {
            return Result.Failed;
        }
    }
}
