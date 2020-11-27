using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PingLog
{
    public class Program
    {
        public static readonly string Name = "pinglog";
        public static bool DoWork = false;

        public static bool show_help = false;       //Implemented
        public static bool endless = false;         //Implemented
        public static ulong max_messages = 4;       //Implemented
        public static ushort size = 32;             //Implemented
        public static bool dont_fragment = false;   //Implemented
        public static byte max_ttl = 128;           //Implemented
        public static int response_timeout = 4000;  //Implemented
        public static int request_timeout = 1000;   //Implemented
        public static AddressFamily protocol;       //Implemented
        public static string source_file;
        public static string? destination_folder;   //Implemented

        public static List<(IPAddress Address, (ulong Sent, ulong Received, ulong Lost) MessagesCounter, List<long> RoundtripTimeValues)> Results;
        public static int AddressFieldWidth = 0;
        public static List<PingTask> pingTasks;

        private static bool results_showed;

        static void Main(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;

            Task main = MainAsync(args);
            main.Wait();
        }

        private static async Task MainAsync(string[] args)
        {
            #region args
            var p = new OptionSet() {
                "Usage: pinglog [OPTIONS]+",
                "\tPinging a single or a list of addresses by sending packages with output to files.",
                "",
                "Options:",
                { "h|?|help", "Show this message and exit.",
                    v => show_help = v != null },
                { "t", $"Specifies ping continue sending echo Request messages to the destination until interrupted. To interrupt and display statistics, press CTRL+ENTER. To interrupt and quit this command, press CTRL+C.",
                    v => endless = v != null },
                { "n=", $"Specifies the number of echo Request messages be sent. The default is {max_messages}. The maximum is 18,446,744,073,709,551,615.",
                    (ulong v) => max_messages = v },
                { "l=", $"Specifies the length, in bytes, of the Data field in the echo Request messages. The default is {size}. The maximum size is 65,527.",
                    (ushort v) => size = v },
                { "f", $"Specifies that echo Request messages are sent with the Do not Fragment flag in the IP header set to 1 (available on IPv4 only). The echo Request message can't be fragmented by routers in the path to the destination. This parameter is useful for troubleshooting path Maximum Transmission Unit (PMTU) problems.",
                    v => dont_fragment = v != null },
                { "i=", $"Specifies the value of the Time To Live (TTL) field in the IP header for echo Request messages sent. The default is {max_ttl}. The maximum TTL is 255.",
                    (byte v) => max_ttl = v },
                { "w=", $"Specifies the amount of time, in milliseconds, to wait for the echo Reply message corresponding to a given echo Request message. If the echo Reply message is not received within the time-out, the 'Request timed out' error message is displayed. The default time-out is {response_timeout} ({TimeSpan.FromMilliseconds(response_timeout).TotalSeconds} sec). The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => response_timeout = v },
                { "s=", $"Not implemented yet.\nSpecifies ...",
                    (string v) => source_file = v },
                { "d:", $"Specifies ...",
                    (string v) => {
                        if (v == null) destination_folder = Directory.GetCurrentDirectory();
                        else destination_folder = v; } },
                { "W=", $"Specifies the amount of time, in milliseconds, to wait for ... . The default time-out is {request_timeout} ({TimeSpan.FromMilliseconds(request_timeout).TotalSeconds} sec). The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => request_timeout = v },
                { "4", $"Specifies IPv4 used to ping. This parameter is not required to identify the target host with an IPv4 address. It is only required to identify the target host by name.",
                    v => protocol = AddressFamily.InterNetwork },
                { "6", $"Specifies IPv6 used to ping. This parameter is not required to identify the target host with an IPv6 address. It is only required to identify the target host by name.",
                    v => protocol = AddressFamily.InterNetworkV6 },
            };
            #endregion

            Console.WriteLine();

            List<string> extra;

            try { extra = p.Parse(args); }
            catch (OptionException e)
            {
                Console.Write($"{Name}: {e.Message} Try `pinglog --help' for more information.");
                return;
            }

            if (show_help || extra.Count == 0)
            {
                p.WriteOptionDescriptions(Console.Out);
                return;
            }

            DoWork = true;

            pingTasks = new List<PingTask>();
            Results = new List<(IPAddress, (ulong, ulong, ulong), List<long>)>();

            foreach (string s in extra)
            {
                var t = new PingTask();
                if (t.Create(s))
                    pingTasks.Add(t);
            }

            foreach (var t in pingTasks) { new Task(async () => t.Run()).Start(); }

            ShowResults(false);
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("Canceled. Waiting last packages...");

            DoWork = false;
            ShowResults(true);

            while (!results_showed) Task.Delay(100);
        }

        private static void ShowResults(bool isCanceled)
        {
            while (pingTasks.Count > 0) Task.Delay(100);
            if (isCanceled) return;

            foreach (var r in Results)
            {
                Console.WriteLine();

                if (r.MessagesCounter.Sent > 0)
                {
                    Console.WriteLine($"\tPackages to {r.Address}:" +
                        $"\n\t\tSent = {r.MessagesCounter.Sent}" +
                        $", Received = {r.MessagesCounter.Received}" +
                        $", Lost = {r.MessagesCounter.Lost}" +
                        ", ({0:0.##}% loss)", ((float)r.MessagesCounter.Lost / (float)r.MessagesCounter.Sent) * 100);

                    if (r.RoundtripTimeValues.Count > 0)
                        Console.WriteLine($"\tTime-outs to {r.Address}:" +
                            $"\n\t\tMinimum = {r.RoundtripTimeValues.Min()}ms" +
                            $", Maximum = {r.RoundtripTimeValues.Max()}ms" +
                            $", Average = {r.RoundtripTimeValues.Average().ToString().Split(',').First()}ms");
                }
            }

            results_showed = true;
        }
    }
}
