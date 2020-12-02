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
        public static bool hide_output;             //Implemented
#nullable enable
        public static string? source_file;          //Implemented
        public static string? destination_folder;   //Implemented
#nullable disable

        public static List<(IPAddress Address, (ulong Sent, ulong Received, ulong Lost) MessagesCounter, List<long> RoundtripTimeValues)> Results;
        public static int AddressFieldWidth = 0;
        public static List<PingTask> pingTasks;

        private static bool results_showed;
        private static List<string> extra;

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
                $"Usage: {Name}\t[-t] [-n=<nubmer>] [-l=<size>] [-f] [-i=<TTL>]\n\t\t[-w=<timeout>] [-h] [-4] [-6] [-W=<timeout>]\n\t\t[-s=<file>] [[-d] | [-d=<directory>]] <hostlist>",
                "",
                "Options:",
                { "?|help", "Show this message and exit.",
                    v => show_help = true },
                { "t", $"Specifies {Name} continue sending echo Request messages to the destination until interrupted. To interrupt, display statistics and quit, press CTRL+C. Will display message if specified.",
                    v => endless = true },
                { "n=", $"Specifies the number of echo Request messages be sent. The default is {max_messages}. The maximum is 18,446,744,073,709,551,615.",
                    (ulong v) => max_messages = v },
                { "l=", $"Specifies the length, in bytes, of the Data field in the echo Request messages. The default is {size}. The maximum size is 65,527.",
                    (ushort v) => size = v },
                { "f", $"Specifies that echo Request messages are sent with the Do not Fragment flag in the IP header set to 1 (available on IPv4 only). The echo Request message can't be fragmented by routers in the path to the destination. This parameter is useful for troubleshooting path Maximum Transmission Unit (PMTU) problems.",
                    v => dont_fragment = true },
                { "i=", $"Specifies the value of the Time To Live (TTL) field in the IP header for echo Request messages sent. The default is {max_ttl}. The maximum TTL is 255.",
                    (byte v) => max_ttl = v },
                { "w=", $"Specifies the amount of time, in milliseconds, to wait for the echo Reply message corresponding to a given echo Request message. If the echo Reply message is not received within the time-out, the 'Request timed out' error message is displayed. The default time-out is {response_timeout} ({TimeSpan.FromMilliseconds(response_timeout).TotalSeconds} sec). The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => response_timeout = v },
                { "h", $"Specifies ... Will display message if specified.",
                    v => hide_output = true },
                { "s=", $"Specifies ... Will display message if specified and {Name} cannot read the file.",
                    (string v) => source_file = v },
                { "d:", $"Specifies ... Will display message if specified.",
                    (string v) => {
                        if (v == null) destination_folder = Directory.GetCurrentDirectory();
                        else destination_folder = v; } },
                { "W=", $"Specifies the amount of time, in milliseconds, to wait for ... . The default time-out is {request_timeout} ({TimeSpan.FromMilliseconds(request_timeout).TotalSeconds} sec). The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => request_timeout = v },
                { "4", $"Specifies IPv4 used to ping. This parameter is not required to identify the target host with an IPv4 address. It is only required to identify the target host by name.",
                    v => protocol = AddressFamily.InterNetwork },
                { "6", $"Specifies IPv6 used to ping. This parameter is not required to identify the target host with an IPv6 address. It is only required to identify the target host by name.",
                    v => protocol = AddressFamily.InterNetworkV6 }, };
            #endregion

            try { extra = p.Parse(args); }
            catch (OptionException e) {
                Console.Write($"{Name}: {e.Message} Try `{Name} --help' for more information.");
                return; }

            if (source_file != null) Read_SourceFile();
            if (destination_folder != null) Check_DestinationDirectory();

            if (show_help || extra.Count == 0) {
                p.WriteOptionDescriptions(Console.Out);
                return; }

            if (hide_output) Console.WriteLine($"Key h specified. {Name} will hide output for each package.");
            if (destination_folder != null) Console.WriteLine($"Key d specified. {Name} will logging output to files.");
            if (endless) Console.WriteLine($"Key t specified. {Name} will continue sending echo Request messages to the destination until interrupted. To interrupt, display statistics and quit, press CTRL+C.\n");

            pingTasks = new List<PingTask>();
            Results = new List<(IPAddress, (ulong, ulong, ulong), List<long>)>();
            DoWork = true;

            foreach (string s in extra) pingTasks.Add(new PingTask(s));
            pingTasks.RemoveAll(t => t.IsFinished());
            foreach (var t in pingTasks) { new Task(async () => t.Run()).Start(); }

            Close(false);
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            if (e.SpecialKey == ConsoleSpecialKey.ControlC) {
                Console.WriteLine("Canceled. Waiting last packages...");

                DoWork = false;

                Close(true);

                while (!results_showed) Task.Delay(100); }

            if (e.SpecialKey == ConsoleSpecialKey.ControlBreak) {
                ShowResults();
                e.Cancel = true; }
        }

        private static void Close(bool isCanceled)
        {
            while (pingTasks.Count > 0) Task.Delay(100);
            if (isCanceled) return;

            ShowResults();

            results_showed = true;
        }

        private static void ShowResults()
        {
            var results = Results;

            foreach (var t in pingTasks)
                results.Add(t.GetResults());

            if (results.Count > 0)
                foreach (var (Address, MessagesCounter, RoundtripTimeValues) in results) {
                    Console.WriteLine();

                    if (MessagesCounter.Sent > 0) {
                        Console.WriteLine($"\tPackages to {Address}:" +
                            $"\n\t\tSent = {MessagesCounter.Sent}" +
                            $", Received = {MessagesCounter.Received}" +
                            $", Lost = {MessagesCounter.Lost}" +
                            ", ({0:0.##}% loss)", ((float)MessagesCounter.Lost / (float)MessagesCounter.Sent) * 100);

                        if (RoundtripTimeValues.Count > 0)
                            Console.WriteLine($"\tTime-outs to {Address}:" +
                                $"\n\t\tMinimum = {RoundtripTimeValues.Min()}ms" +
                                $", Maximum = {RoundtripTimeValues.Max()}ms" +
                                ", Average = {0:0.##}ms)", RoundtripTimeValues.Average()); } }
        }

        private static void Read_SourceFile()
        {
            if (!File.Exists(source_file)) {
                Console.WriteLine($"Key s specified, but file \"{source_file}\" is not exist. {Name} will ignore this key.");
                return; }

            string line;
            StreamReader source = new StreamReader(source_file);

            while ((line = source.ReadLine()) != null) extra.Add(line);
        }

        private static void Check_DestinationDirectory()
        {
            if (!Directory.Exists(destination_folder)) {
                Console.WriteLine($"Key d specified, but directory \"{destination_folder}\" is not exist. {Name} will ignore this key.");
                destination_folder = null;
                return; }
        }
    }
}
