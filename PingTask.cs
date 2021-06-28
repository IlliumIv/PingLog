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
        private bool isFinished = false;
        private string log;
        private string destDesc;
        private int index;

        public PingTask(string s)
        {
            string output = $"Pinging {s}";
            destDesc = s;
            index = GetHashCode();

            if (IPAddress.TryParse(destDesc, out IPAddress address)) { }
            else {
                try {
                    IPHostEntry hostEntry = Dns.GetHostEntry(destDesc);

                    if (Program.protocol != AddressFamily.Unspecified)
                        address = hostEntry.AddressList.First(a => a.AddressFamily == Program.protocol);
                    else address = hostEntry.AddressList.First();

                    output += $" [{address}]";
                    destDesc += $" [{address}]"; }

                catch (Exception e) {
                    Console.WriteLine($"{output} failed: {e.Message}");

                    isFinished = true;

                    return; } }

            Console.WriteLine($"{output} with {Program.size} bytes of data...");

            Program.Results.Add(GetHashCode(), (address, new Dictionary<string, ulong> { { "Sent", 0 }, { "Received", 0 }, { "Lost", 0 } } , new List<long>()));

            if (address.ToString().Length > Program.AddressFieldWidth)
                Program.AddressFieldWidth = address.ToString().Length;
        }

        public async Task Run()
        {
            var i = GetHashCode();

            byte[] buffer = new byte[Program.size];
            string address = destDesc;

            if (Program.destination_folder != null) {
                log = $"ping_{destDesc}_{DateTime.Now}".Replace(" ", "_");
                log = String.Join(".", log.Split(Path.GetInvalidFileNameChars()));
                log = Path.Combine(Program.destination_folder + $"{Path.DirectorySeparatorChar}{log}.csv");

                var dir = new FileInfo(log).Directory.FullName;

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.Create(log).Close();
                File.AppendAllText(log, $"Date;{destDesc};Bytes [{buffer.Length}];Time;TTL"); }

            PingOptions options = new PingOptions(Program.max_ttl, Program.dont_fragment);
            Ping pingSender = new Ping();

            switch (Program.endless) {
                case false:
                    for (ulong j = 0; j < Program.max_messages; j++) {
                        if (Program.DoWork == false) break;

                        Send(pingSender, buffer, options);

                        if (j + 1 != Program.max_messages) await Task.Delay(Program.request_timeout); } break;

                case true:
                    while (Program.DoWork) {
                        Send(pingSender, buffer, options);

                        await Task.Delay(Program.request_timeout); } break; }

            isFinished = true;

            File.AppendAllText(log, $"\n" +
                $"\nSent;{Program.Results[index].MessagesCounter["Sent"]}" +
                $"\nReceived;{Program.Results[index].MessagesCounter["Received"]}" +
                $"\nLost;{Program.Results[index].MessagesCounter["Lost"]}" +
                $"\n% Loss;={((float)Program.Results[index].MessagesCounter["Lost"]/(float)Program.Results[index].MessagesCounter["Sent"])*100}" +
                $"\n" +
                $"\nMinimum Time;{Program.Results[index].RoundtripTimeValues.Min()}" +
                $"\nMaximum Time;{Program.Results[index].RoundtripTimeValues.Max()}" +
                $"\nAverage Time;{Program.Results[index].RoundtripTimeValues.Average()}");
        }

        public bool IsFinished()
        {
            return isFinished;
        }

        private void Send(Ping pingSender, byte[] buffer, PingOptions options)
        {
            PingReply reply;
            string console_output = $"{DateTime.Now}\t";
            string log_output = $"{DateTime.Now};";

            Program.Results[index].MessagesCounter["Sent"]++;
            Program.Results[index].MessagesCounter["Lost"]++;

            try {
                reply = pingSender.Send(Program.Results[index].Address, Program.response_timeout, buffer, options);

                if (reply.Status == IPStatus.Success) {
                    Program.Results[index].MessagesCounter["Received"]++;
                    Program.Results[index].MessagesCounter["Lost"]--;
                    Program.Results[index].RoundtripTimeValues.Add(reply.RoundtripTime);
                    console_output += $"Reply from {Program.Results[index].Address.ToString().PadRight(Program.AddressFieldWidth)}:" +
                                $"\tbytes={reply.Buffer.Length}" +
                                $"\ttime={reply.RoundtripTime}ms";
                    log_output += $"Reply from {Program.Results[index].Address} received" +
                                $";{reply.Buffer.Length}" +
                                $";{reply.RoundtripTime}";

                    if (reply.Options != null) {
                        console_output += $"\tTTL={reply.Options.Ttl}";
                        log_output += $";{reply.Options.Ttl}"; } }
                else {
                    console_output += $"Reply from {Program.Results[index].Address.ToString().PadRight(Program.AddressFieldWidth)}:" +
                        $"\t{reply.Status.ToString().SplitCamelCase()}";
                    log_output += $";{reply.Status.ToString().SplitCamelCase()}";

                    if (reply.Status == IPStatus.TimedOut) {
                        console_output += $"\ttime={Program.response_timeout}ms";
                        log_output += $";{Program.response_timeout}"; } } }

            catch (Exception e) {
                console_output = $"{DateTime.Now}\t {e.GetAllMessages()}";
                log_output += $";{e.GetAllMessages()}"; }

            if (Program.destination_folder != null) File.AppendAllTextAsync(log, "\n" + log_output);
            if (!Program.hide_output) Console.WriteLine(console_output);
        }
    }
}