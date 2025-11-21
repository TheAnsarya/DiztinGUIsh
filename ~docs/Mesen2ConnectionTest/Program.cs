using System;
using System.Threading.Tasks;
using Diz.Import.mesen.tracelog;

namespace Mesen2ConnectionTest
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("=================================================");
            Console.WriteLine("Mesen2 Live Streaming Connection Test");
            Console.WriteLine("=================================================");
            Console.WriteLine();
            
            string host = "localhost";
            int port = 9998;
            
            if (args.Length >= 1)
                host = args[0];
            if (args.Length >= 2)
                int.TryParse(args[1], out port);
            
            Console.WriteLine($"Target: {host}:{port}");
            Console.WriteLine();
            Console.WriteLine("Creating MesenLiveTraceClient...");
            
            using var client = new MesenLiveTraceClient();
            client.ConnectTimeoutMs = 5000;
            client.ReceiveTimeoutMs = 5000;
            
            Console.WriteLine("Attempting connection...");
            Console.WriteLine();
            
            var connected = await client.ConnectAsync(host, port);
            
            Console.WriteLine();
            if (connected)
            {
                Console.WriteLine("SUCCESS! Connected to Mesen2!");
                Console.WriteLine($"Connection stats: {client.GetConnectionStats()}");
                Console.WriteLine();
                Console.WriteLine("Waiting 5 seconds to receive data...");
                
                await Task.Delay(5000);
                
                Console.WriteLine($"Final stats: {client.GetConnectionStats()}");
                Console.WriteLine();
                Console.WriteLine("Disconnecting...");
                client.Disconnect();
                Console.WriteLine("Disconnected.");
            }
            else
            {
                Console.WriteLine("FAILED to connect to Mesen2!");
                Console.WriteLine();
                Console.WriteLine("Troubleshooting:");
                Console.WriteLine("1. Is Mesen2 running?");
                Console.WriteLine("2. Is a ROM loaded in Mesen2?");
                Console.WriteLine("3. Did you run 'emu.startDiztinguishServer(9998)' in Lua console?");
                Console.WriteLine("4. Check Windows Firewall settings");
            }
            
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey();
        }
    }
}
