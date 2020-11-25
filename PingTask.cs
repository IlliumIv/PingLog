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
        static string address_str;
        static (ulong Sent, ulong Received, ulong Lost) messages_counter;
        static List<long> roundtripTime_values;
        static IPAddress address;

        public static (IPAddress Address, (ulong, ulong, ulong) MessagesCounter, List<long> RoundtripTimeValues) Results = (address, messages_counter, roundtripTime_values);

        public PingTask(string s)
        {
            address_str = s;
        }

        public async Task<(IPAddress, (ulong, ulong, ulong), List<long>)?> Run()
        {
            string output = $"Pinging {address_str}";

#pragma warning disable CS0642 // Possible mistaken empty statement
            if (IPAddress.TryParse(address_str, out address)) ;
#pragma warning restore CS0642 // Possible mistaken empty statement
            else
            {
                try
                {
                    IPHostEntry hostEntry = Dns.GetHostEntry(address_str);

                    if (Program.protocol != AddressFamily.Unspecified)
                        address = hostEntry.AddressList.First(a => a.AddressFamily == Program.protocol);
                    else
                        address = hostEntry.AddressList.First();

                    output += $" [{address}]";
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Pinging {address_str} failed: {e.GetaAllMessages()}");
                    return null;
                }
            }

            output += $" with {Program.size} bytes of data:\n";
            Console.WriteLine(output);

            messages_counter = (0, 0, 0);
            roundtripTime_values = new List<long>();

            byte[] buffer = new byte[Program.size];
            PingOptions options = new PingOptions(Program.max_ttl, Program.dont_fragment);

            Ping pingSender = new Ping();

            for (ulong i = 0; i < Program.max_messages; i++)
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
                        console_output += $"{Program.SplitCamelCase(reply.Status.ToString())}";
                        if (reply.Status == IPStatus.TimedOut) console_output += $" ({Program.response_timeout}ms)";

                        messages_counter.Lost++;
                    }
                }
                catch (PingException e) { console_output += e.GetaAllMessages(); }

                Console.WriteLine(console_output);

                if (i + 1 != Program.max_messages)
                    Thread.Sleep(Program.request_timeout);
            }

            Console.WriteLine($"\n\tPackages to {address}:\n\t\tSent = {messages_counter.Sent}, Received = {messages_counter.Received}, Lost = {messages_counter.Lost}, ({messages_counter.Lost / messages_counter.Sent * 100}% loss)");

            if (roundtripTime_values.Count > 0)
                Console.WriteLine($"\tTime-outs to {address}:\n\t\tMinimum = {roundtripTime_values.Min()}, Maximum = {roundtripTime_values.Max()}, Average = {roundtripTime_values.Average()}");

            return Results;
        }
    }
}