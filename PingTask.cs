using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace PingLog
{
    public class PingTask
    {
        string log;
        readonly string destDesc;
        readonly int index;

        public PingTask(string s)
        {
            string output = $"Pinging {s}";
            destDesc = s;
            index = GetHashCode();

            if (IPAddress.TryParse(destDesc, out IPAddress address)) { }
            else
            {
                try
                {
                    IPHostEntry hostEntry = Dns.GetHostEntry(destDesc);

                    if (Program.ProtocolVersion != AddressFamily.Unspecified)
                        address = hostEntry.AddressList.First(a => a.AddressFamily == Program.ProtocolVersion);
                    else address = hostEntry.AddressList.First();

                    output += $" [{address}]";
                    destDesc += $" [{address}]";
                }

                catch (Exception e)
                {
                    Console.WriteLine($"{output} failed: {e.Message}");

                    CloseTask();

                    return;
                }
            }

            Console.WriteLine($"{output} with {Program.PackageSize} bytes of data...");

            Program.Results.Add(GetHashCode(), (address, new Dictionary<string, ulong> { { "Sent", 0 }, { "Received", 0 }, { "Lost", 0 } }, new HashSet<long>()));

            if (address.ToString().Length > Program.AddressFieldWidth)
                Program.AddressFieldWidth = address.ToString().Length;
        }

        public async Task Run()
        {
            byte[] buffer = new byte[Program.PackageSize];

            if (Program.DestinationFolder != null)
            {
                log = $"ping_{destDesc}_{DateTime.Now}".Replace(" ", "_");
                log = String.Join(".", log.Split(Path.GetInvalidFileNameChars()));
                log = Path.Combine(Program.DestinationFolder + $"{Path.DirectorySeparatorChar}{log}.csv");
                var dir = new FileInfo(log).Directory.FullName;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.Create(log).Close();
                File.AppendAllText(log, $"Date;{destDesc};Bytes [{buffer.Length}];Time;TTL");
            }

            PingOptions options = new(Program.Ttl, Program.AllowPackageFragmentation);
            Ping pingSender = new();

            switch (Program.Endless)
            {
                case false:
                    for (ulong j = 0; j < Program.MaximumPackages; j++)
                    {
                        if (Program.ShouldContinue == false) break;
                        Send(pingSender, buffer, options);
                        if (j + 1 != Program.MaximumPackages) await Task.Delay(Program.RequestTimeout);
                    }
                    break;

                case true:
                    while (Program.ShouldContinue)
                    {
                        Send(pingSender, buffer, options);
                        await Task.Delay(Program.RequestTimeout);
                    }
                    break;
            }

            CloseTask();

            File.AppendAllText(log, $"\n" +
                $"\nSent;{Program.Results[index].MessagesCounter["Sent"]}" +
                $"\nReceived;{Program.Results[index].MessagesCounter["Received"]}" +
                $"\nLost;{Program.Results[index].MessagesCounter["Lost"]}" +
                $"\n% Loss;={((float)Program.Results[index].MessagesCounter["Lost"] / (float)Program.Results[index].MessagesCounter["Sent"]) * 100}" +
                $"\n" +
                $"\nMinimum Time;{Program.Results[index].RoundtripTimeValues.Min()}" +
                $"\nMaximum Time;{Program.Results[index].RoundtripTimeValues.Max()}" +
                $"\nAverage Time;{Program.Results[index].RoundtripTimeValues.Average()}");
        }

        private void CloseTask()
        {
            Program.Tasks.Remove(this);
            // if (Program.pingTasks.Count == 0) Program.pinging = false;
            if (Program.Tasks.Count == 0) Program.WhilePinging.Set();
        }

        private void Send(Ping pingSender, byte[] buffer, PingOptions options)
        {
            PingReply reply;
            string console_output = $"{DateTime.Now}\t";
            string log_output = $"{DateTime.Now};";

            Program.Results[index].MessagesCounter["Sent"]++;
            Program.Results[index].MessagesCounter["Lost"]++;

            try
            {
                reply = pingSender.Send(Program.Results[index].Address, Program.ResponseTimeout, buffer, options);

                if (reply.Status == IPStatus.Success)
                {
                    Program.Results[index].MessagesCounter["Received"]++;
                    Program.Results[index].MessagesCounter["Lost"]--;
                    Program.Results[index].RoundtripTimeValues.Add(reply.RoundtripTime);
                    console_output += $"Reply from {Program.Results[index].Address.ToString().PadRight(Program.AddressFieldWidth)}:" +
                                $"\tbytes={reply.Buffer.Length}" +
                                $"\ttime={reply.RoundtripTime}ms";
                    log_output += $"Reply from {Program.Results[index].Address} received" +
                                $";{reply.Buffer.Length}" +
                                $";{reply.RoundtripTime}";

                    if (reply.Options != null)
                    {
                        console_output += $"\tTTL={reply.Options.Ttl}";
                        log_output += $";{reply.Options.Ttl}";
                    }
                }
                else
                {
                    console_output += $"Reply from {Program.Results[index].Address.ToString().PadRight(Program.AddressFieldWidth)}:" +
                        $"\t{reply.Status.ToString().SplitCamelCase()}";
                    log_output += $";{reply.Status.ToString().SplitCamelCase()}";

                    if (reply.Status == IPStatus.TimedOut)
                    {
                        console_output += $"\ttime={Program.ResponseTimeout}ms";
                        log_output += $";{Program.ResponseTimeout}";
                    }
                }
            }

            catch (Exception e)
            {
                console_output = $"{DateTime.Now}\t {e.GetAllMessages()}";
                log_output += $";{e.GetAllMessages()}";
            }

            if (Program.DestinationFolder != null) File.AppendAllTextAsync(log, "\n" + log_output);
            if (!Program.ShouldHideOutput) Console.WriteLine(console_output);
        }
    }
}