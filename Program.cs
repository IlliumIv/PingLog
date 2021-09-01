using Mono.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace PingLog
{
    public class Program
    {
        public static bool DoWork = false;
        public static ManualResetEvent WhilePinging = new ManualResetEvent(false);

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

        public static Dictionary<int, (IPAddress Address, Dictionary<string, ulong> MessagesCounter, HashSet<long> RoundtripTimeValues)> Results;
        public static int AddressFieldWidth = 0;
        public static ConcurrentHashSet<PingTask> pingTasks;

        private static readonly string Name = "PingLog";
        private static HashSet<string> extra;

        static void Main(string[] args)
        {
            Console.CancelKeyPress += Console_CancelKeyPress;
            Task main = MainAsync(args);
            main.Wait();
        }

        private static async Task MainAsync(string[] args)
        {
            #region args
            var p = new OptionSet()
            {
                $"Usage: {Name}\t[-t] [-n=<nubmer>] [-l=<size>] [-f] [-i=<TTL>]\n\t\t[-w=<timeout>] [-h] [-4] [-6] [-W=<timeout>]\n\t\t[-s=<file>] [[-d] | [-d=<directory>]] <hostlist>",
                "",
                "Options:",
                { "?|help", Resources.Resource.description_help,
                    v => show_help = true
                },
                { "t", $"Specifies {Name} continue sending echo Request messages to the destination until interrupted. " +
                       $"To display statistic and continue, press CTRL+BREAK (CTRL+\\ on Linux). " +
                       $"To interrupt, display statistics and quit, press CTRL+C. " +
                       $"Will display message if specified.",
                    v => endless = true
                },
                { "n=", $"Specifies the number of echo Request messages be sent. " +
                        $"The default is {max_messages}. " +
                        $"The maximum is 18,446,744,073,709,551,615.",
                    (ulong v) => max_messages = v
                },
                { "l=", $"Specifies the length, in bytes, of the Data field in the echo Request messages. " +
                        $"The default is {size}. " +
                        $"The maximum size is 65,527.",
                    (ushort v) => size = v
                },
                { "f", $"Specifies that echo Request messages are sent with the Do not Fragment flag in the IP header set to 1 (available on IPv4 only).",
                    v => dont_fragment = true
                },
                { "i=", $"Specifies the value of the Time To Live (TTL) field in the IP header for echo Request messages sent. " +
                        $"The default is {max_ttl}. " +
                        $"The maximum TTL is 255.",
                    (byte v) => max_ttl = v
                },
                { "w=", $"Specifies the amount of time, in milliseconds, to wait for the echo Reply message corresponding to a given echo Request message. " +
                        $"The default time-out is {response_timeout} ({TimeSpan.FromMilliseconds(response_timeout).TotalSeconds} sec). " +
                        $"The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => response_timeout = v
                },
                { "h", $"Specifies {Name} to hide the results of attempts to send echo Request messages. " +
                       $"Will display message if specified.",
                    v => hide_output = true
                },
                { "s=", $"Specifies path of the file of destinations list. " +
                        $"Full and relative paths available. " +
                        $"Will display message if specified.",
                    (string v) => source_file = v
                },
                { "d:", $"Enables writing results of attempts to send echo Request messages to a file. " +
                        $"The default path is current working directory. " +
                        $"Full and relative paths available. " +
                        $"Will display message if specified.",
                    (string v) =>
                    {
                        if (v == null || v.Length == 0) destination_folder = Directory.GetCurrentDirectory();
                        else destination_folder = v;
                    }
                },
                { "W=", $"Specifies the amount of time, in milliseconds, to wait between sending each new echo Request message. " +
                        $"The default time-out is {request_timeout} ({TimeSpan.FromMilliseconds(request_timeout).TotalSeconds} sec). " +
                        $"The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => request_timeout = v
                },
                { "4", $"Specifies IPv4 used to ping. " +
                       $"This parameter is only required to identify the target host by name.",
                    v => protocol = AddressFamily.InterNetwork
                },
                { "6", $"Specifies IPv6 used to ping. " +
                       $"This parameter is only required to identify the target host by name.",
                    v => protocol = AddressFamily.InterNetworkV6
                },
            };
            #endregion

            try { extra = p.Parse(args).ToHashSet(); }
            catch (OptionException e)
            {
                Console.Write($"{Name}: {e.Message} Try `{Name} --help' for more information.");
                return;
            }

            if (source_file != null) Read_SourceFile();
            if (destination_folder != null)
            {
                Console.Write($"Key d specified");
                string dirFullPath = Path.GetFullPath(destination_folder);
                if (!Directory.Exists(dirFullPath))
                {
                    Console.Write($", but directory \"{dirFullPath}\" is not exist. {Name} will try to create directory... ");
                    try { Directory.CreateDirectory(dirFullPath); }
                    catch (Exception e)
                    {
                        Console.WriteLine($"{e.Message}");
                        return;
                    };
                    Console.Write($"Success");
                };

                destination_folder = dirFullPath;
                Console.WriteLine($". {Name} will logging output to files in directory \"{destination_folder}{Path.DirectorySeparatorChar}\".");
            };

            if (show_help || extra.Count == 0)
            {
                p.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (hide_output) Console.WriteLine($"Key h specified. {Name} will hide output for each package.");
            if (endless) Console.WriteLine($"Key t specified. {Name} will continue sending echo Request messages to the destination until interrupted. " +
                                           $"To display statistic and continue, press CTRL+BREAK (CTRL+\\ on Linux). " +
                                           $"To interrupt, display statistics and quit, press CTRL+C.");

            if (destination_folder != null || hide_output || endless) Console.WriteLine();

            pingTasks = new ConcurrentHashSet<PingTask>();
            DoWork = true;

            Results = new Dictionary<int, (IPAddress Address, Dictionary<string, ulong> MessagesCounter, HashSet<long> RoundtripTimeValues)>();

            foreach (string s in extra) pingTasks.Add(new PingTask(s));
            foreach (var t in pingTasks) { new Task(async () => t.Run()).Start(); }

            WhilePinging.WaitOne();
            ShowResults();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            if (e.SpecialKey == ConsoleSpecialKey.ControlC)
            {
                DoWork = false;
                if (pingTasks != null) foreach (var task in pingTasks) if (task == null) pingTasks.Remove(task);
            }

            if (e.SpecialKey == ConsoleSpecialKey.ControlBreak)
            {
                ShowResults();
                Console.WriteLine();
            }
        }

        public static void ShowResults()
        {
            foreach (var kv in Results)
                if (kv.Value.MessagesCounter["Sent"] > 0)
                {
                    Console.WriteLine($"\tPackages to {kv.Value.Address}:" +
                        $"\n\t\tSent = {kv.Value.MessagesCounter["Sent"]}" +
                        $", Received = {kv.Value.MessagesCounter["Received"]}" +
                        $", Lost = {kv.Value.MessagesCounter["Lost"]}" +
                        ", ({0:0.##}% loss)", ((float)kv.Value.MessagesCounter["Lost"] / (float)kv.Value.MessagesCounter["Sent"]) * 100);

                    if (kv.Value.RoundtripTimeValues.Count > 0)
                        Console.WriteLine($"\tTime-outs to {kv.Value.Address}:" +
                            $"\n\t\tMinimum = {kv.Value.RoundtripTimeValues.Min()}ms" +
                            $", Maximum = {kv.Value.RoundtripTimeValues.Max()}ms" +
                            ", Average = {0:0.##}ms", kv.Value.RoundtripTimeValues.Average());
                }
        }

        private static void Read_SourceFile()
        {
            if (!File.Exists(source_file))
            {
                Console.WriteLine($"Key s specified, but file \"{source_file}\" does not exist. {Name} will ignore this key.");
                return;
            }

            string line;
            StreamReader source = new StreamReader(source_file);

            while ((line = source.ReadLine()) != null) extra.Add(line);
        }
    }
}
