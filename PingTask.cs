using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PingLog
{
    public class PingTask
    {
        private (ulong Sent, ulong Received, ulong Lost) messages_counter = (0, 0, 0);
        private List<long> roundtripTime_values = new List<long>();
        private IPAddress address;

        private bool isFinished = false;
        private (IPAddress, (ulong, ulong, ulong), List<long>) results;

        public PingTask(string s)
        {
            string output = $"Pinging {s}";

            if (IPAddress.TryParse(s, out address)) ;
            else
            {
                try
                {
                    IPHostEntry hostEntry = Dns.GetHostEntry(s);

                    if (Program.protocol != AddressFamily.Unspecified)
                        address = hostEntry.AddressList.First(a => a.AddressFamily == Program.protocol);
                    else
                        address = hostEntry.AddressList.First();

                    output += $" [{address}]";
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Pinging {s} failed: {e.GetaAllMessages()}");
                    isFinished = true;
                    return;
                }
            }


            output += $" with {Program.size} bytes of data:";
            Console.WriteLine(output);
        }

        public async Task Run()
        {
            byte[] buffer = new byte[Program.size];
            PingOptions options = new PingOptions(Program.max_ttl, Program.dont_fragment);

            Ping pingSender = new Ping();

            switch (Program.endless)
            {
                case false:
                    for (ulong i = 0; i < Program.max_messages; i++)
                    {
                        Send(pingSender, buffer, options);

                        if (Program.DoWork == false)
                            break;

                        if (i + 1 != Program.max_messages)
                            await Task.Delay(Program.request_timeout);
                    }
                    isFinished = true;
                    results = (address, messages_counter, roundtripTime_values);
                    // Console.WriteLine($"Ended ping is done: {IsFinished()}");

                    break;

                case true:
                    while(Program.DoWork)
                    {
                        Send(pingSender, buffer, options);
                        await Task.Delay(Program.request_timeout);
                    }
                    isFinished = true;
                    results = (address, messages_counter, roundtripTime_values);
                    // Console.WriteLine($"Endless ping is done: {IsFinished()}");

                    break;
            }

        }

        public bool IsFinished()
        {
            return isFinished;
        }

        public (IPAddress, (ulong, ulong, ulong), List<long>) GetResults()
        {
            return results;
        }

        private void Send(Ping pingSender, byte[] buffer, PingOptions options)
        {
            PingReply reply;
            messages_counter.Sent++;
            string console_output = $"{DateTime.Now}\t";

            try
            {
                reply = pingSender.Send(address, Program.response_timeout, buffer, options);
                if (reply.Status == IPStatus.Success)
                {
                    messages_counter.Received++;
                    roundtripTime_values.Add(reply.RoundtripTime);

                    switch (address.AddressFamily)
                    {
                        case AddressFamily.InterNetwork:
                            // IPv4
                            console_output += $"Reply from {address}: bytes={reply.Buffer.Length} time={reply.RoundtripTime}ms TTL={reply.Options.Ttl}";
                            break;
                        case AddressFamily.InterNetworkV6:
                            // IPv6
                            console_output += $"Reply from {address}: bytes={reply.Buffer.Length} time={reply.RoundtripTime}ms";
                            break;
                        default:
                            // Something is wrong
                            break;
                    }
                }
                else
                {
                    console_output += $"{reply.Status.ToString().SplitCamelCase()}";
                    if (reply.Status == IPStatus.TimedOut) console_output += $" ({Program.response_timeout}ms)";

                    messages_counter.Lost++;
                }
            }
            catch (PingException e) { console_output += e.GetaAllMessages(); }

            Console.WriteLine(console_output);
        }
    }
}