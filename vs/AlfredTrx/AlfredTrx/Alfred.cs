﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TaskRunnerExplorer;

namespace AlfredTrx
{
    public static class Alfred
    {
        private static Task<bool> _initTask;
        private static PowerShell _ps;

        public static string ModulePath { get; private set; }

        public static async Task EnsureInitialized(ITaskRunnerCommandContext context)
        {
            TaskCompletionSource<bool> tcs = new TaskCompletionSource<bool>();
            Task existing = Interlocked.CompareExchange(ref _initTask, tcs.Task, null);

            //If we own the initialization...
            if (existing == null)
            {
                _ps = PowerShell.Create();
                _ps.AddScript("Set-ExecutionPolicy -Scope Process -ExecutionPolicy Unrestricted –Force");
                _ps.Invoke();
                _ps.Commands.Clear();

                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                ModulePath = Path.Combine(localAppData, "Ligershark\\tools\\alfredps-pre\\alfred.psm1");

                // you can override the location of the PSModule with this env var
                string modulePathEnv = Environment.GetEnvironmentVariable("AlfredPsModulePath");
                if (!string.IsNullOrWhiteSpace(modulePathEnv) && File.Exists(modulePathEnv)) {
                    ModulePath = modulePathEnv;
                }

                //If we don't already have alfred installed, install it
                if (!File.Exists(ModulePath))
                {
                    await InstallAlfredAsync();
                }

                _ps.Commands.Clear();
                Command importModule = new Command("Import-Module");
                importModule.Parameters.Add("Name", ModulePath);
                _ps.Commands.AddCommand(importModule);
                _ps.Invoke();

                tcs.SetResult(true);
                return;
            }

            await existing;
        }

        internal static IEnumerable<string> DiscoverTasksIn(string configPath)
        {
            _ps.Commands.Clear();
            Command listCommand = new Command("alfred");
            listCommand.Parameters.Add("scriptPath", configPath);
            listCommand.Parameters.Add("list");
            _ps.Commands.AddCommand(listCommand);
            IEnumerable<PSObject> result = _ps.Invoke();
            dynamic names = (dynamic)_ps.Runspace.SessionStateProxy.GetVariable("alfredcontext");
            IEnumerable<string> taskNames = ((IEnumerable)names.Tasks.Keys).OfType<string>();
            return taskNames.Select(x => x.Trim()).OrderBy(x => x);
        }

        private static async Task InstallAlfredAsync()
        {
            HttpWebRequest request = WebRequest.CreateHttp("https://raw.githubusercontent.com/sayedihashimi/alfredps/master/getalfred.ps1");
            request.UserAgent = "AlfredTRX-VS" + typeof(ITaskRunner).Assembly.GetName().Version;
            WebResponse response = await request.GetResponseAsync();
            Stream responseStream = response.GetResponseStream();
            string alfredSource;

            using (StreamReader reader = new StreamReader(responseStream))
            {
                alfredSource = await reader.ReadToEndAsync();
            }

            _ps.AddScript(alfredSource);
            _ps.Invoke();
        }
    }
}
