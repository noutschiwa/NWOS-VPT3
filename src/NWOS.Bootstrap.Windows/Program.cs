namespace NWOS.Bootstrap.Windows;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        try
        {
            return await BootstrapCommands.RunAsync(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NWOS Bootstrap] Start fehlgeschlagen: {ex}");
            return 1;
        }
    }
}
