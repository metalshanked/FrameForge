using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FrameForge;

internal static class SingleInstance
{
    private static string PipeName => "FrameForge.Activate." + Environment.UserName + "." + Process.GetCurrentProcess().SessionId;
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    public static async Task<bool> NotifyAsync(string? file)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            var pidBytes = new byte[4];
            await pipe.ReadExactlyAsync(pidBytes, timeout.Token);
            int pid = BitConverter.ToInt32(pidBytes);
            if (pid > 0) AllowSetForegroundWindow(pid);
            byte[] message = Encoding.UTF8.GetBytes(file ?? "");
            if (message.Length > 32768) return false;
            await pipe.WriteAsync(BitConverter.GetBytes(message.Length), timeout.Token);
            await pipe.WriteAsync(message, timeout.Token);
            await pipe.FlushAsync(timeout.Token);
            return true;
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException) { return false; }
    }

    public static async Task ListenAsync(Action<string?> activate, CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellation);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                await pipe.WriteAsync(BitConverter.GetBytes(Environment.ProcessId), timeout.Token);
                await pipe.FlushAsync(timeout.Token);
                var header = new byte[4];
                await pipe.ReadExactlyAsync(header, timeout.Token);
                int length = BitConverter.ToInt32(header);
                if (length < 0 || length > 32768) continue;
                var data = new byte[length];
                await pipe.ReadExactlyAsync(data, timeout.Token);
                activate(length == 0 ? null : Encoding.UTF8.GetString(data));
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or UnauthorizedAccessException)
            {
                if (cancellation.IsCancellationRequested) return;
                await Task.Delay(100, cancellation);
            }
        }
    }
}
