﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Neo4jManager.V3
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class Neo4jV3JavaInstanceProvider : Neo4jV3ProcessBasedInstanceProvider, INeo4jInstance
    {
        private const int defaultWaitForKill = 10000;

        private readonly string javaPath;

        private Process process;


        public Neo4jV3JavaInstanceProvider(string javaPath, string neo4jHomeFolder, IFileCopy fileCopy, Neo4jVersion neo4jVersion, Neo4jEndpoints endpoints)
            :base(neo4jHomeFolder, fileCopy, neo4jVersion, endpoints)
        {
            this.javaPath = javaPath;
        }

        public override async Task Start(CancellationToken token)
        {
            if (process == null)
            {
                process = GetProcess();
                process.Start();
                await this.WaitForReady(token);

                Status = Status.Started;
                return;
            }

            if (!process.HasExited) return;
            
            process.Start();
            await this.WaitForReady(token);
            Status = Status.Started;
        }

        public override async Task Stop(CancellationToken token)
        {
            Status = Status.Stopping;
            await Task.Run(() =>
            {
                Stop();
            }, token);
        }

        public Status Status { get; private set; } = Status.Stopped;

        private void Stop()
        {
            if (process == null || process.HasExited) return;

            process.Kill();
            process.WaitForExit(defaultWaitForKill);

            Status = Status.Stopped;
        }

        public void Dispose()
        {
            Stop();

            process?.Dispose();
        }

        private Process GetProcess()
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = GetJavaCmdArguments(),
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
        }

        private string GetJavaCmdArguments()
        {
            var builder = new StringBuilder();

            builder
                .Append(" -cp ")
                .Append(quotes)
                .Append($"{neo4jHomeFolder}/lib/*;{neo4jHomeFolder}/plugins/*")
                .Append(quotes);

            builder.Append(" -server");

            builder.Append(" -Dlog4j.configuration=file:conf/log4j.properties");
            builder.Append(" -Dneo4j.ext.udc.source=zip-powershell");
            builder.Append(" -Dorg.neo4j.cluster.logdirectory=data/log");

            var jvmAdditionalParams = configEditors[Neo4jConfigFile]
                .FindValues("dbms.jvm.additional")
                .Select(p => p.Value);

            foreach (var param in jvmAdditionalParams)
            {
                builder.Append($" {param}");
            }

            var heapInitialSize = configEditors[Neo4jConfigFile].GetValue("dbms.memory.heap.initial_size");
            if (!string.IsNullOrEmpty(heapInitialSize))
            {
                builder.Append($" -Xms{heapInitialSize}");
            }
            var heapMaxSize = configEditors[Neo4jConfigFile].GetValue("dbms.memory.heap.max_size");
            if (!string.IsNullOrEmpty(heapMaxSize))
            {
                builder.Append($" -Xmx{heapMaxSize}");
            }

            builder
                .Append(" org.neo4j.server.CommunityEntryPoint")
                .Append(" --config-dir=")
                .Append(quotes)
                .Append($@"{neo4jHomeFolder}\conf")
                .Append(quotes)
                .Append(" --home-dir=")
                .Append(quotes)
                .Append(neo4jHomeFolder)
                .Append(quotes);

            return builder.ToString();
        }
    }
}
