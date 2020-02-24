using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Nest;

namespace CountersToElk
{
    public class ESClient
    {
        private static ESClient _instance;

        private readonly string _appName;

        private readonly ElasticClient _client;
        private readonly string _index;
        private readonly string _machineName = Environment.MachineName;

        private ESClient()
        {
            var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", false, true)
                .Build();
            var conSettings = new ConnectionSettings(new Uri(config.GetSection("ElasticSearch:Host").Value));
            if (Debugger.IsAttached) conSettings.EnableDebugMode();
            _client =
                new ElasticClient(conSettings);

            _appName = config.GetSection("ApplicationName").Value;
            _index = config.GetSection("ElasticSearch:Index").Value;
        }

        public static ESClient Instance => _instance ??= new ESClient();


        public void Persist(List<ICounterPayload> payloads)
        {
            var now = DateTime.Now;
            var documents = payloads.Select(payload => new Document
            {
                Application = _appName,
                MachineName = _machineName,
                PCName = payload.GetName(),
                PCDisplayName = payload.GetDisplay(),
                PCValue = payload.GetValue(),
                PCType = payload.GetCounterType(),
                TimeStamp = now
            });

            var response = _client.IndexMany(documents, _index);
            Console.WriteLine(response.ApiCall.ToString());
        }

        private class Document
        {
            public string Application { get; set; }
            public string MachineName { get; set; }
            public string PCName { get; set; }
            public string PCDisplayName { get; set; }
            public double PCValue { get; set; }
            public string PCType { get; set; }
            public DateTime TimeStamp { get; set; }
        }
    }
}