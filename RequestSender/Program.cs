﻿using LoadBalancer.Exceptions;
using LoadBalancer.Logger;
using LoadBalancer.RequestCache;
using Microsoft.Extensions.DependencyInjection;

namespace LoadBalancer
{
    public class Program
    {
        private static async Task SimulateTraffic(LoadBalancer loadBalancer)
        {
            List<(int DurationInSeconds, int RequestsPerSecond)> trafficPatterns = new()
            {
                //for 10 sec, send 1 req
                (10, 1),
                //for 60 sec, send 10 req
                (20, 500),
                (20, 3000),
                (10, 1),
                (20, 700),
                (20, 5),
                (30, 500),
            };

            foreach (var pattern in trafficPatterns)
            {
                Log.Info($"Simulating traffic: {pattern.RequestsPerSecond} requests/second for {pattern.DurationInSeconds} seconds.");

                var tasks = new List<Task>();
                var endTime = DateTime.UtcNow.AddSeconds(pattern.DurationInSeconds);

                while (DateTime.UtcNow < endTime)
                {
                    for (int i = 0; i < pattern.RequestsPerSecond; i++)
                    {
                        tasks.Add(Task.Run(async () =>
                        {
                            var dummyRequest = new HttpRequestMessage(HttpMethod.Get, "http://localhost");
                            var wasRequestHandled = await loadBalancer.HandleRequestAsync(dummyRequest);
                        }));
                    }

                    await Task.Delay(5000);
                }

                await Task.WhenAll(tasks);
                Log.Info($"Finished traffic simulation: {pattern.RequestsPerSecond} requests/second.\n\n");
            }
        }

        public static async Task Main(string[] args)
        {
            LoadBalancer? loadBalancer = null;
            try
            {

                Log.AddSink(
                    LogSinks.ConsoleAndFile,
                    Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                        "LoadBalancerLogs"
                    )
                );
                Log.SetMinimumLevel(LogLevel.TRC);

                var services = new ServiceCollection();
                services.AddSingleton<LoadBalancer>(provider =>
                {
                    var loadBalancer = new LoadBalancer(
                        loadBalancingStrategy: new RoundRobinStrategy(),
                        httpClient: new HttpClient(),
                        enabledAutoScaling: true,
                        autoScalingConfig: AutoScalingConfig.Factory(),
                        healthCheckInterval: TimeSpan.FromSeconds(10),
                        minHealthThreshold: 90
                    );
                    loadBalancer.Initialize(TimeSpan.FromSeconds(10));

                    return loadBalancer;
                });

                var serviceProvider = services.BuildServiceProvider();
                loadBalancer = serviceProvider.GetRequiredService<LoadBalancer>();

                await SimulateTraffic(loadBalancer);
            }
            catch (Exception ex) //catch all
            {
                Log.Error($"An error occurred {ex.GetType().FullName}", ex);
                Environment.Exit(1);
            }
            finally 
            {
                await loadBalancer!.DestroyAsync();
            }
        }    
    }
}
