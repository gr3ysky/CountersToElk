using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Extensions.Configuration;
using Polly;

namespace CountersToElk
{
    public class Program
    {
        private const int METRIC_COUNT = 23;
        private static readonly List<ICounterPayload> _buffer = new List<ICounterPayload>();
        private static readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1);

        public static void Main(string[] args)
        {
            Console.WriteLine("Started");
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", false, true)
                .Build();

            var appName = config.GetSection("ApplicationName").Value;
            var interval = int.Parse(config.GetSection("SampleIntervalInSeconds").Value);

            var policy = Policy.Handle<Exception>().WaitAndRetryForever(c => TimeSpan.FromSeconds(interval));

            policy.Execute(() => Run(appName, config));
        }

        private static void Run(string appName, IConfigurationRoot config)
        {
            var pid = FindPidByAppName(appName);
            if (pid == -1)
            {
                Console.WriteLine("Application is not running...");
                throw new Exception("Application is not running...");
            }

            var client = new DiagnosticsClient(pid);
            var intervalSeconds = config.GetSection("SampleIntervalInSeconds").Value;

            var evtProvider =
                EventPipeProviderHelper.ToProvider(
                    $"System.Runtime:0xffffffff:5:EventCounterIntervalSec={intervalSeconds}");
            var hostingProvider =
                EventPipeProviderHelper.ToProvider(
                    $"Microsoft.AspNetCore.Hosting:0x0:4:EventCounterIntervalSec={intervalSeconds}");
            var session = client.StartEventPipeSession(new[] {evtProvider, hostingProvider}, false);
            var source = new EventPipeEventSource(session.EventStream);
            source.Dynamic.All += Dynamic_All;
            source.Process();
            Console.ReadKey();
            session.Dispose();
            source.Dispose();
        }

        private static void Dynamic_All(TraceEvent obj)
        {
            if (obj.EventName.Equals("EventCounters"))
            {
                var payloadVal = (IDictionary<string, object>) obj.PayloadValue(0);
                var payloadFields = (IDictionary<string, object>) payloadVal["Payload"];
                var payload = payloadFields["CounterType"].Equals("Sum")
                    ? new IncrementingCounterPayload(payloadFields, 1000)
                    : (ICounterPayload) new CounterPayload(payloadFields);
                _buffer.Add(payload);
            }

            _semaphore.Wait();
            if (_buffer.Count != METRIC_COUNT)
            {
                _semaphore.Release();
            }
            else
            {
                ESClient.Instance.Persist(_buffer);
                _buffer.Clear();
                _semaphore.Release();
            }
        }

        private static int FindPidByAppName(string appName)
        {
            var process = DiagnosticsClient
                .GetPublishedProcesses()
                .Select(GetProcessById).FirstOrDefault(p => p != null && p.ProcessName.ToLower() == appName.ToLower());
            if (process != null)
                Console.WriteLine("{0} {1} {2}", process.ProcessName, process.Id, process.MainModule.FileName);

            return process?.Id ?? -1;
        }

        private static Process GetProcessById(int processId)
        {
            try
            {
                return Process.GetProcessById(processId);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}