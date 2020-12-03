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

        public PingTask(string s)
        {
            string output = $"Pinging {s}";

            if (IPAddress.TryParse(s, out IPAddress address)) { }
            else {
                try {
                    IPHostEntry hostEntry = Dns.GetHostEntry(s);

                    if (Program.protocol != AddressFamily.Unspecified)
                        address = hostEntry.AddressList.First(a => a.AddressFamily == Program.protocol);
                    else address = hostEntry.AddressList.First(); }

                catch (Exception e) {
                    Console.WriteLine($"{output} failed: {e.Message}");

                    isFinished = true;

                    return; } }

            Console.WriteLine($"{output} [{address}] with {Program.size} bytes of data...");

            Program.Results.Add(GetHashCode(), (address, new Dictionary<string, ulong> { { "Sent", 0 }, { "Received", 0 }, { "Lost", 0 } } , new List<long>()));

            if (address.ToString().Length > Program.AddressFieldWidth)
                Program.AddressFieldWidth = address.ToString().Length;
        }

        public async Task Run()
        {
            var i = GetHashCode();

            if (Program.destination_folder != null) {
                log = $"ping_[{Program.Results[i].Address}]_{DateTime.Now}".Replace(" ", "_");
                log = String.Join(".", log.Split(Path.GetInvalidFileNameChars()));
                log = Path.Combine(Program.destination_folder + $"\\{log}.csv");

                var dir = new FileInfo(log).Directory.FullName;

                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.Create(log).Close();
                File.AppendAllText(log, $"Date;{Program.Results[i].Address};Bytes;Time;TTL"); }

            byte[] buffer = new byte[Program.size];
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
        }

        public bool IsFinished()
        {
            return isFinished;
        }

        /*
        public (IPAddress, (ulong, ulong, ulong), List<long>) GetResults()
        {
            return results;
        }
        */

        private void Send(Ping pingSender, byte[] buffer, PingOptions options)
        {
            var i = GetHashCode();

            PingReply reply;
            string console_output = $"{DateTime.Now}\t";
            string log_output = $"{DateTime.Now};";

            Program.Results[i].MessagesCounter["Sent"]++;
            Program.Results[i].MessagesCounter["Lost"]++;

            try {
                reply = pingSender.Send(Program.Results[i].Address, Program.response_timeout, buffer, options);

                // Console.WriteLine(reply.Status);

                if (reply.Status == IPStatus.Success) {
                    Program.Results[i].MessagesCounter["Received"]++;
                    Program.Results[i].MessagesCounter["Lost"]--;
                    Program.Results[i].RoundtripTimeValues.Add(reply.RoundtripTime);
                    console_output += $"Reply from {Program.Results[i].Address.ToString().PadRight(Program.AddressFieldWidth)}:" +
                                $"\tbytes={reply.Buffer.Length}" +
                                $"\ttime={reply.RoundtripTime}ms";
                    log_output += $"Reply from {Program.Results[i].Address} received" +
                                $";{reply.Buffer.Length}" +
                                $";{reply.RoundtripTime}";

                    if (Program.Results[i].Address.AddressFamily == AddressFamily.InterNetwork) {
                        console_output += $"\tTTL={reply.Options.Ttl}";
                        log_output += $";{reply.Options.Ttl}"; } }
                else {
                    console_output += $"Reply from {Program.Results[i].Address.ToString().PadRight(Program.AddressFieldWidth)}:" +
                        $"\t{reply.Status.ToString().SplitCamelCase()}";
                    log_output += $";{reply.Status.ToString().SplitCamelCase()}";

                    if (reply.Status == IPStatus.TimedOut) {
                        console_output += $"\ttime={Program.response_timeout}ms";
                        log_output += $";{Program.response_timeout}"; } } }

            catch (PingException e) {
                console_output += e.GetaAllMessages();
                log_output += $";{e.GetaAllMessages()}"; }

            if (Program.destination_folder != null) File.AppendAllTextAsync(log, "\n" + log_output);
            if (!Program.hide_output) Console.WriteLine(console_output);
        }
    }
}