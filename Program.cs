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
        static bool _continue = false;
        public static bool ShouldContinue
        {
            get => _continue;
            set { _continue = value; }
        }

        static ManualResetEvent _pinging = new(false);
        public static ManualResetEvent WhilePinging
        {
            get => _pinging;
            set { _pinging = value; }
        }

        static bool _showHelp = false;
        public static bool ShowHelp
        {
            get => _showHelp;
            set { _showHelp = value; }
        }

        static bool _endless = false;
        public static bool Endless
        {
            get => _endless;
            set { _endless = value; }
        }

        static ulong _maxMessages = 4;
        public static ulong MaximumPackages
        {
            get => _maxMessages;
            set { _maxMessages = value; }
        }

        static ushort _size = 32;
        public static ushort PackageSize
        {
            get => _size;
            set { _size = value; }
        }

        static bool _fragment = false;
        public static bool AllowPackageFragmentation
        {
            get => _fragment;
            set { _fragment = value; }
        }

        static byte _ttl = 128;
        public static byte Ttl
        {
            get => _ttl;
            set { _ttl = value; }
        }

        static int _timeoutResponse = 4000;
        public static int ResponseTimeout
        {
            get => _timeoutResponse;
            set { _timeoutResponse = value; }
        }

        static int _timeoutRequest = 1000;
        public static int RequestTimeout
        {
            get => _timeoutRequest;
            set { _timeoutRequest = value; }
        }

        static AddressFamily _protocol;
        public static AddressFamily ProtocolVersion
        {
            get => _protocol;
            set { _protocol = value; }
        }

        static bool _hideOutput;
        public static bool ShouldHideOutput
        {
            get => _hideOutput;
            set { _hideOutput = value; }
        }
#nullable enable
        static string? _source;
        public static string? SourceFile
        {
            get => _source;
            set { _source = value; }
        }

        static string? _destination;
        public static string? DestinationFolder
        {
            get => _destination;
            set { _destination = value; }
        }
#nullable disable

        static Dictionary<int, (IPAddress, Dictionary<string, ulong>, HashSet<long>)> _results;
        public static Dictionary<int, (IPAddress Address, Dictionary<string, ulong> MessagesCounter, HashSet<long> RoundtripTimeValues)> Results
        {
            get => _results;
            set { _results = value; }
        }

        static int _fieldWidth = 0;
        public static int AddressFieldWidth
        {
            get => _fieldWidth;
            set { _fieldWidth = value; }
        }

        static ConcurrentHashSet<PingTask> _tasks;
        public static ConcurrentHashSet<PingTask> Tasks
        {
            get => _tasks;
            set { _tasks = value; }
        }

        static string Name => typeof(Program).Assembly.GetName().Name;
        static HashSet<string> _extra;

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
                    v => ShowHelp = true
                },
                { "t", $"Specifies {Name} continue sending echo Request messages to the destination until interrupted. " +
                       $"To display statistic and continue, press CTRL+BREAK (CTRL+\\ on Linux). " +
                       $"To interrupt, display statistics and quit, press CTRL+C. " +
                       $"Will display message if specified.",
                    v => Endless = true
                },
                { "n=", $"Specifies the number of echo Request messages be sent. " +
                        $"The default is {MaximumPackages}. " +
                        $"The maximum is 18,446,744,073,709,551,615.",
                    (ulong v) => MaximumPackages = v
                },
                { "l=", $"Specifies the length, in bytes, of the Data field in the echo Request messages. " +
                        $"The default is {PackageSize}. " +
                        $"The maximum size is 65,527.",
                    (ushort v) => PackageSize = v
                },
                { "f", $"Specifies that echo Request messages are sent with the Do not Fragment flag in the IP header set to 1 (available on IPv4 only).",
                    v => AllowPackageFragmentation = true
                },
                { "i=", $"Specifies the value of the Time To Live (TTL) field in the IP header for echo Request messages sent. " +
                        $"The default is {Ttl}. " +
                        $"The maximum TTL is 255.",
                    (byte v) => Ttl = v
                },
                { "w=", $"Specifies the amount of time, in milliseconds, to wait for the echo Reply message corresponding to a given echo Request message. " +
                        $"The default time-out is {ResponseTimeout} ({TimeSpan.FromMilliseconds(ResponseTimeout).TotalSeconds} sec). " +
                        $"The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => ResponseTimeout = v
                },
                { "h", $"Specifies {Name} to hide the results of attempts to send echo Request messages. " +
                       $"Will display message if specified.",
                    v => ShouldHideOutput = true
                },
                { "s=", $"Specifies path of the file of destinations list. " +
                        $"Full and relative paths available. " +
                        $"Will display message if specified.",
                    (string v) => SourceFile = v
                },
                { "d:", $"Enables writing results of attempts to send echo Request messages to a file. " +
                        $"The default path is current working directory. " +
                        $"Full and relative paths available. " +
                        $"Will display message if specified.",
                    (string v) =>
                    {
                        if (v == null || v.Length == 0) DestinationFolder = Directory.GetCurrentDirectory();
                        else DestinationFolder = v;
                    }
                },
                { "W=", $"Specifies the amount of time, in milliseconds, to wait between sending each new echo Request message. " +
                        $"The default time-out is {RequestTimeout} ({TimeSpan.FromMilliseconds(RequestTimeout).TotalSeconds} sec). " +
                        $"The maximum time-out is 2,147,483,647 (~25 days).",
                    (int v) => RequestTimeout = v
                },
                { "4", $"Specifies IPv4 used to ping. " +
                       $"This parameter is only required to identify the target host by name.",
                    v => ProtocolVersion = AddressFamily.InterNetwork
                },
                { "6", $"Specifies IPv6 used to ping. " +
                       $"This parameter is only required to identify the target host by name.",
                    v => ProtocolVersion = AddressFamily.InterNetworkV6
                },
            };
            #endregion

            try { _extra = p.Parse(args).ToHashSet(); }
            catch (OptionException e)
            {
                Console.Write($"{Name}: {e.Message} Try `{Name} --help' for more information.");
                return;
            }

            if (SourceFile != null) Read_SourceFile();
            if (DestinationFolder != null)
            {
                Console.Write($"Key d specified");
                string dirFullPath = Path.GetFullPath(DestinationFolder);
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

                DestinationFolder = dirFullPath;
                Console.WriteLine($". {Name} will logging output to files in directory \"{DestinationFolder}{Path.DirectorySeparatorChar}\".");
            };

            if (ShowHelp || _extra.Count == 0)
            {
                p.WriteOptionDescriptions(Console.Out);
                return;
            }

            if (ShouldHideOutput) Console.WriteLine($"Key h specified. {Name} will hide output for each package.");
            if (Endless) Console.WriteLine($"Key t specified. {Name} will continue sending echo Request messages to the destination until interrupted. " +
                                           $"To display statistic and continue, press CTRL+BREAK (CTRL+\\ on Linux). " +
                                           $"To interrupt, display statistics and quit, press CTRL+C.");

            if (DestinationFolder != null || ShouldHideOutput || Endless) Console.WriteLine();

            Tasks = new ConcurrentHashSet<PingTask>();
            ShouldContinue = true;

            Results = new Dictionary<int, (IPAddress Address, Dictionary<string, ulong> MessagesCounter, HashSet<long> RoundtripTimeValues)>();

            foreach (string s in _extra) Tasks.Add(new PingTask(s));
            foreach (var t in Tasks) { new Task(async () => t.Run()).Start(); }

            WhilePinging.WaitOne();
            ShowResults();
        }

        private static void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;

            if (e.SpecialKey == ConsoleSpecialKey.ControlC)
            {
                ShouldContinue = false;
                if (Tasks != null) foreach (var task in Tasks) if (task == null) Tasks.Remove(task);
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
            if (!File.Exists(SourceFile))
            {
                Console.WriteLine($"Key s specified, but file \"{SourceFile}\" does not exist. {Name} will ignore this key.");
                return;
            }

            string line;
            var source = new StreamReader(SourceFile);

            while ((line = source.ReadLine()) != null) _extra.Add(line);
        }
    }
}