using God2.ServerV2.Core;

Console.WriteLine("God2 Server V2");
Console.WriteLine($"Version: {ServerV2Architecture.Version}");
Console.WriteLine($"Default content mode: {ServerContentMode.ClassicCompatibility}");
Console.WriteLine("Foundation host is online. Network/runtime services will be added under M1.");

foreach (var milestone in ServerV2Architecture.Milestones)
{
    Console.WriteLine($"- {milestone}");
}
