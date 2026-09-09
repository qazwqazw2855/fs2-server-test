using God2.ClassicServer.ConsoleHost;
using System.Text;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

using var shutdownSignal = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdownSignal.Cancel();
};
Console.CancelKeyPress += cancelHandler;
try
{
    return await ConsoleEntry.RunAsync(args, shutdownSignal.Token);
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}
