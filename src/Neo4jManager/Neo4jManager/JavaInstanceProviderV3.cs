﻿using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Neo4jManager
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class JavaInstanceProviderV3 : INeo4jInstanceProvider
    {
        private const string quotes = "\"";
        private const string defaultDataDirectory = "data/databases";
        private const string defaultActiveDatabase = "graph.db";
        private const int defaultWaitForKill = 10000;

        private readonly string javaPath;
        private readonly string neo4jHomeFolder;
        private readonly IFileCopy fileCopy;
        private readonly ConfigEditor configEditor;

        private Process process;

        public JavaInstanceProviderV3(string javaPath, string neo4jHomeFolder, Neo4jEndpoints endpoints, IFileCopy fileCopy)
        {
            this.javaPath = javaPath;
            this.neo4jHomeFolder = neo4jHomeFolder;
            this.fileCopy = fileCopy;

            var configFile = Path.Combine(neo4jHomeFolder, "conf/neo4j.conf");
            configEditor = new ConfigEditor(configFile);

            Endpoints = endpoints;
        }

        public async Task Start()
        {
            if (process == null)
            {
                process = GetProcess();
                process.Start();
                await this.WaitForReady();

                return;
            }

            if (!process.HasExited) return;
            
            process.Start();
            await this.WaitForReady();
        }

        public async Task Stop()
        {
            if (process == null || process.HasExited) return;

            await Task.Run(() =>
            {
                process.Kill();
                process.WaitForExit(defaultWaitForKill);
            });
        }

        public void Configure(string key, string value)
        {
            configEditor.SetValue(key, value);
        }

        public async Task Clear()
        {
            var dataPath = GetDataPath();

            await Stop();
            Directory.Delete(dataPath);
            await Start();
        }

        public async Task Backup(string destinationPath, bool stopInstanceBeforeBackup = true)
        {
            var dataPath = GetDataPath();

            if (stopInstanceBeforeBackup) await Stop();
            fileCopy.MirrorFolders(dataPath, destinationPath);
            if (stopInstanceBeforeBackup) await Start();
        }

        public async Task Restore(string sourcePath)
        {
            var dataPath = GetDataPath();

            await Stop();
            fileCopy.MirrorFolders(sourcePath, dataPath);
            await Start();
        }

        public Neo4jEndpoints Endpoints { get; }

        public void Dispose()
        {
            Stop().Wait();

            process?.Dispose();
        }

        private string GetDataPath()
        {
            var dataDirectory = configEditor.GetValue("dbms.directories.data");
            if (string.IsNullOrEmpty(dataDirectory))
                dataDirectory = defaultDataDirectory;

            var activeDatabase = configEditor.GetValue("dbms.active_database");
            if (string.IsNullOrEmpty(activeDatabase))
                activeDatabase = defaultActiveDatabase;

            return Path.Combine(neo4jHomeFolder, dataDirectory, activeDatabase);
        }

        private Process GetProcess()
        {
            return new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = javaPath,
                    Arguments = GetJavaCmdArguments()
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

            var jvmAdditionalParams = configEditor
                .FindValues("dbms.jvm.additional")
                .Select(p => p.Value);

            foreach (var param in jvmAdditionalParams)
            {
                builder.Append($" {param}");
            }

            var heapInitialSize = configEditor.GetValue("dbms.memory.heap.initial_size");
            if (!string.IsNullOrEmpty(heapInitialSize))
            {
                builder.Append($" -Xms{heapInitialSize}");
            }
            var heapMaxSize = configEditor.GetValue("dbms.memory.heap.max_size");
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
