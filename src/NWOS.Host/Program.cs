using System;
using System.Threading.Tasks;

namespace NWOS.Host
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            Console.WriteLine("[Cardinal] Starte NWOS-Komponente...");

            try
            {
                // Übergabe an die zentrale Host-Komposition, die Cardinal Kernel,
                // IPC-Dienste und Module (Alya, Alice, Marc etc.) initialisiert.
                await HostComposition.RunAsync(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Cardinal] Schwerwiegender Fehler im Host: {ex.Message}");
                Environment.ExitCode = 1;
            }
        }
    }
}