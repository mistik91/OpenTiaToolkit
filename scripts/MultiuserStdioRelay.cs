using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace OpenTiaToolkit.Harness
{
    // Transport only. No framing changes, JSON parsing, command injection, or TIA process ownership.
    public static class MultiuserStdioRelay
    {
        private static async Task Pump(Stream source, Stream destination)
        {
            var buffer = new byte[8192];
            int count;
            while ((count = await source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
            {
                await destination.WriteAsync(buffer, 0, count).ConfigureAwait(false);
                // Process pipe FileStreams buffer small messages. Interactive MCP must
                // deliver every chunk without waiting for a full buffer or stdin EOF.
                await destination.FlushAsync().ConfigureAwait(false);
            }
        }

        private static async Task ForwardInput(Process child)
        {
            try
            {
                await Pump(Console.OpenStandardInput(), child.StandardInput.BaseStream).ConfigureAwait(false);
            }
            finally
            {
                try { child.StandardInput.Close(); } catch (IOException) { }
            }
        }

        public static int Forward(Process child, int shutdownSeconds)
        {
            if (shutdownSeconds < 1 || shutdownSeconds > 120)
                throw new ArgumentOutOfRangeException("shutdownSeconds");
            Task input = Task.Run(() => ForwardInput(child));
            Task output = Task.Run(() => Pump(child.StandardOutput.BaseStream, Console.OpenStandardOutput()));
            Task error = Task.Run(() => Pump(child.StandardError.BaseStream, Console.OpenStandardError()));
            Stopwatch stopping = null;
            while (!child.WaitForExit(100))
            {
                if (input.IsCompleted || output.IsFaulted || error.IsFaulted)
                {
                    if (stopping == null)
                    {
                        stopping = Stopwatch.StartNew();
                        try { child.StandardInput.Close(); } catch (IOException) { }
                    }
                    if (stopping.Elapsed.TotalSeconds >= shutdownSeconds)
                    {
                        Console.Error.WriteLine("Agent MCP relay shutdown incomplete. Child PID {0} is still running; inspect its active operation before retrying. No process was forcibly terminated.", child.Id);
                        return 124;
                    }
                }
            }
            if (!Task.WaitAll(new[] { output, error }, 5000))
            {
                Console.Error.WriteLine("Agent MCP relay could not drain the child's protocol streams.");
                return 125;
            }
            // A closed child can break its stdin while a client is still writing; its exit is authoritative.
            if (input.IsFaulted) { var observed = input.Exception; }
            return child.ExitCode;
        }
    }
}
